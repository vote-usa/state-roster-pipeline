using System.Text;
using System.Text.RegularExpressions;

namespace StateBallot.Tools;

/// <summary>
/// Reconstructs MySQL DDL for VoteUSA roster tables from Vote.designer.cs
/// InitAllColumns. VoteProject has no checked-in CREATE TABLE dump.
/// </summary>
internal static class VoteSchemaExtractor
{
    internal static readonly string[] Tables =
    [
        "States",
        "Counties",
        "Parties",
        "Offices",
        "Elections",
        "Politicians",
        "ElectionsOffices",
        "ElectionsPoliticians",
        "Referendums",
        // Console auth + local jurisdiction lookup (not roster data, but read by the pipeline).
        "Security",
        "LocalDistricts",
    ];

    private static readonly Dictionary<string, string[]> PrimaryKeys = new()
    {
        ["States"] = ["StateCode"],
        ["Counties"] = ["StateCode", "CountyCode"],
        ["Parties"] = ["PartyKey"],
        ["Offices"] = ["OfficeKey"],
        ["Elections"] = ["ElectionKey"],
        ["Politicians"] = ["PoliticianKey"],
        ["ElectionsOffices"] = ["ElectionKey", "OfficeKey"],
        ["ElectionsPoliticians"] = ["ElectionKey", "OfficeKey", "PoliticianKey"],
        ["Referendums"] = ["ReferendumKey"],
        ["Security"] = ["UserName"],
        ["LocalDistricts"] = ["StateCode", "LocalKey"],
    };

    private static readonly Regex ColumnRegex = new(
        """_column = new DataColumn\("(?<name>[^"]+)", typeof\((?<type>[^)]+)\)\);(?<body>.*?)base\.Columns\.Add\(_column\);""",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex MaxLengthRegex = new(@"MaxLength = (\d+)", RegexOptions.Compiled);

    internal static (string Sql, int ColumnCount) Generate(string designer, string regenComment)
    {
        var tables = ExtractTables(designer);
        var chunks = new List<string>
        {
            "-- Generated from VoteProject.5 Vote.designer.cs InitAllColumns.",
            "-- Roster-relevant tables only. Not a full VoteUSA dump.",
            regenComment,
            "SET NAMES utf8mb4;",
            "SET FOREIGN_KEY_CHECKS = 0;",
        };

        foreach (var name in Tables)
        {
            chunks.Add($"DROP TABLE IF EXISTS `{name}`;");
            chunks.Add(EmitTable(name, tables[name]));
            chunks.Add("");
        }

        chunks.Add(
            """
            -- Pipeline-only. VoteProject has no home for source_url / ocd_division_id.
            -- Needed to round-trip the data-repo JSON/CSV from this DB.
            DROP TABLE IF EXISTS `RosterProvenance`;
            CREATE TABLE `RosterProvenance` (
              `EntityType` VARCHAR(32) NOT NULL,
              `EntityKey` VARCHAR(190) NOT NULL,
              `SourceUrl` TEXT NULL,
              `OcdDivisionId` VARCHAR(255) NULL,
              `SourceCandidateId` VARCHAR(100) NULL,
              `ExtraJson` JSON NULL,
              PRIMARY KEY (`EntityType`, `EntityKey`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);
        chunks.Add("SET FOREIGN_KEY_CHECKS = 1;");
        return (string.Join("\n", chunks) + "\n", tables.Values.Sum(v => v.Count));
    }

    internal static Dictionary<string, List<ColumnDef>> ExtractTables(string designer)
    {
        var found = new Dictionary<string, List<ColumnDef>>(StringComparer.Ordinal);
        foreach (var table in Tables)
        {
            var marker = $"this.TableName = \"{table}\"";
            var idx = designer.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
                throw new InvalidOperationException($"TableName {table} not found in designer");

            var initIdx = designer.LastIndexOf("private void InitAllColumns()", idx, StringComparison.Ordinal);
            if (initIdx < 0)
                throw new InvalidOperationException($"InitAllColumns not found before {table}");

            var brace = designer.IndexOf('{', initIdx);
            if (brace < 0)
                throw new InvalidOperationException($"Unclosed InitAllColumns for {table}");

            var end = MatchingBrace(designer, brace);
            var cols = ParseInitAllColumns(designer[brace..(end + 1)]);
            if (cols.Count == 0)
                throw new InvalidOperationException($"No columns parsed for {table}");
            found[table] = cols;
        }

        return found;
    }

    private static int MatchingBrace(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
                depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        throw new InvalidOperationException("Unclosed InitAllColumns");
    }

    private static List<ColumnDef> ParseInitAllColumns(string block)
    {
        var cols = new List<ColumnDef>();
        foreach (Match m in ColumnRegex.Matches(block))
        {
            var body = m.Groups["body"].Value;
            var maxMatch = MaxLengthRegex.Match(body);
            cols.Add(new ColumnDef(
                m.Groups["name"].Value,
                m.Groups["type"].Value,
                maxMatch.Success ? int.Parse(maxMatch.Groups[1].Value) : null,
                !body.Contains("AllowDBNull = false", StringComparison.Ordinal)));
        }

        return cols;
    }

    private static string EmitTable(string name, List<ColumnDef> cols)
    {
        var pk = PrimaryKeys[name];
        var colSql = new List<string>();
        foreach (var col in cols)
        {
            if (col.Name == "Id" && col.Type == "Int32")
            {
                colSql.Add("  `Id` INT NOT NULL AUTO_INCREMENT");
                continue;
            }

            var typ = SqlType(col.Type, col.MaxLength);
            colSql.Add($"  `{col.Name}` {typ} {SqlNullAndDefault(col.Type, col.Nullable, typ)}");
        }

        colSql.Add($"  PRIMARY KEY ({string.Join(", ", pk.Select(c => $"`{c}`"))})");
        if (cols.Any(c => c.Name == "Id"))
            colSql.Add("  UNIQUE KEY `Id` (`Id`)");

        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE `{name}` (");
        sb.AppendLine(string.Join(",\n", colSql));
        sb.Append(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;");
        return sb.ToString();
    }

    private static string SqlType(string csType, int? maxLength) => csType switch
    {
        "String" when maxLength is null or > 512 => "TEXT",
        "String" => $"VARCHAR({maxLength})",
        "Boolean" => "TINYINT(1)",
        "Int32" => "INT",
        "DateTime" => "DATETIME",
        "Decimal" => "DECIMAL(12,4)",
        "TimeSpan" => "TIME",
        "Byte[]" => "LONGBLOB",
        _ => throw new InvalidOperationException($"Unhandled designer type: {csType}"),
    };

    private static string SqlNullAndDefault(string csType, bool nullable, string sqlT)
    {
        var blob = sqlT is "TEXT" or "LONGBLOB" || sqlT.StartsWith("TEXT", StringComparison.Ordinal);
        if (blob)
            return "NULL";
        if (nullable)
            return "NULL";
        return csType switch
        {
            "String" => "NOT NULL DEFAULT ''",
            "Boolean" or "Int32" or "Decimal" => "NOT NULL DEFAULT 0",
            "DateTime" => "NOT NULL DEFAULT '1900-01-01 00:00:00'",
            "TimeSpan" => "NOT NULL DEFAULT '00:00:00'",
            _ => "NOT NULL",
        };
    }

    internal sealed record ColumnDef(string Name, string Type, int? MaxLength, bool Nullable);
}
