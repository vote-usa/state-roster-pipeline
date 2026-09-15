-- Restructures the staging store so the unit of record is one election, not one state.
--
-- A CLI invocation collects a whole state and year in one fetch, because that is how the
-- sources publish. That fetch is a CollectionPass. It fans out into one Runs row per
-- election, which is what gets reviewed, resolved and loaded.
--
-- Runs absorbs the old RunElections columns (a run has exactly one election), so
-- RunCandidates and RunMeasures now hang off RunId alone. State-level artifacts that
-- belong to no election (county directory, statewide measures with no ballot yet) hang
-- off the pass.
--
-- Drops and recreates the 0001 run tables. The staging store is a prototype harness with
-- no data worth migrating, and the shape change is not additive.

DROP TABLE IF EXISTS `RunCandidates`;
DROP TABLE IF EXISTS `RunMeasures`;
DROP TABLE IF EXISTS `RunElections`;
DROP TABLE IF EXISTS `Runs`;

CREATE TABLE `CollectionPasses` (
  `PassId` INT NOT NULL AUTO_INCREMENT,
  `StateCode` VARCHAR(2) NOT NULL,
  `Year` INT NOT NULL,
  `Status` VARCHAR(16) NOT NULL DEFAULT 'queued',   -- queued | running | succeeded | failed
  `Source` VARCHAR(8) NOT NULL DEFAULT 'cli',       -- cli | web
  `RequestedBy` VARCHAR(100) NOT NULL DEFAULT '',
  `RequestedAt` DATETIME NOT NULL,
  `StartedAt` DATETIME NULL,
  `FinishedAt` DATETIME NULL,
  `CancelRequested` TINYINT(1) NOT NULL DEFAULT 0,
  `ElectionFilter` DATE NULL,                       -- set when the invocation targeted one election
  `CliArgs` TEXT NULL,
  `GitSha` VARCHAR(40) NULL,
  `Wayback` VARCHAR(14) NULL,
  `RunCount` INT NOT NULL DEFAULT 0,                -- elections this pass produced runs for
  `CandidateCount` INT NOT NULL DEFAULT 0,
  `MeasureCount` INT NOT NULL DEFAULT 0,
  `CountyBallotCount` INT NOT NULL DEFAULT 0,
  `CountyDirectoryCount` INT NOT NULL DEFAULT 0,
  `ProposedMeasureCount` INT NOT NULL DEFAULT 0,
  `UnassignedRowCount` INT NOT NULL DEFAULT 0,      -- rows whose election could not be identified
  `GapCount` INT NOT NULL DEFAULT 0,
  `GapsJson` JSON NULL,
  `SourcesJson` JSON NULL,                          -- provenance per data group, was sources.json
  `Summary` TEXT NULL,
  `LogText` MEDIUMTEXT NULL,
  `ErrorText` TEXT NULL,
  PRIMARY KEY (`PassId`),
  KEY `ix_passes_state_requested` (`StateCode`, `RequestedAt`),
  KEY `ix_passes_status` (`Status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `Runs` (
  `RunId` INT NOT NULL AUTO_INCREMENT,
  `PassId` INT NOT NULL,
  `StateCode` VARCHAR(2) NOT NULL,
  `Year` INT NOT NULL,
  `ElectionDate` DATE NOT NULL,
  `ElectionType` VARCHAR(50) NOT NULL DEFAULT '',
  `ElectionName` VARCHAR(255) NOT NULL DEFAULT '',
  `SourceElectionId` VARCHAR(50) NOT NULL DEFAULT '',
  `Jurisdiction` VARCHAR(200) NOT NULL DEFAULT '',
  `OcdDivisionId` VARCHAR(255) NULL,
  `SourceUrl` TEXT NULL,
  `Status` VARCHAR(16) NOT NULL DEFAULT 'succeeded',  -- succeeded | pending
  `IsPending` TINYINT(1) NOT NULL DEFAULT 0,          -- source has not published ballot data yet
  `CandidateCount` INT NOT NULL DEFAULT 0,
  `MeasureCount` INT NOT NULL DEFAULT 0,
  `CountyBallotCount` INT NOT NULL DEFAULT 0,
  `CreatedAt` DATETIME NOT NULL,
  `ElectionKey` VARCHAR(18) NULL,                     -- VoteUSA key once resolved
  `ResolveStatus` VARCHAR(16) NULL,                   -- resolved | new | unresolved
  `ResolveReason` VARCHAR(100) NULL,
  PRIMARY KEY (`RunId`),
  KEY `ix_runs_pass` (`PassId`),
  KEY `ix_runs_election` (`StateCode`, `ElectionDate`),
  KEY `ix_runs_status` (`Status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `RunCandidates` (
  `RunCandidateId` INT NOT NULL AUTO_INCREMENT,
  `RunId` INT NOT NULL,
  `StateCode` VARCHAR(2) NOT NULL,
  `ElectionDate` DATE NOT NULL,
  `ElectionType` VARCHAR(50) NOT NULL DEFAULT '',
  `Office` VARCHAR(255) NOT NULL DEFAULT '',
  `District` VARCHAR(255) NULL,
  `County` VARCHAR(255) NULL,
  `OcdDivisionId` VARCHAR(255) NULL,
  `CandidateName` VARCHAR(255) NOT NULL DEFAULT '',
  `Party` VARCHAR(100) NULL,
  `Incumbent` TINYINT(1) NULL,
  `SourceUrl` TEXT NULL,
  `SourceCandidateId` VARCHAR(100) NULL,
  `FilingDate` VARCHAR(30) NULL,
  `Email` VARCHAR(255) NULL,
  `Phone` VARCHAR(50) NULL,
  `CampaignPhone` VARCHAR(50) NULL,
  `Website` TEXT NULL,
  `Occupation` VARCHAR(255) NULL,
  `MailingAddressLine` VARCHAR(255) NULL,
  `MailingCity` VARCHAR(100) NULL,
  `MailingState` VARCHAR(20) NULL,
  `MailingZip` VARCHAR(20) NULL,
  `ResidentialCity` VARCHAR(100) NULL,
  `ResidentialCounty` VARCHAR(100) NULL,
  `SourceOfficeId` VARCHAR(100) NULL,
  `SourceOfficeType` VARCHAR(100) NULL,
  `LocalJurisdiction` VARCHAR(255) NULL,
  `FirstName` VARCHAR(100) NULL,
  `MiddleName` VARCHAR(100) NULL,
  `LastName` VARCHAR(100) NULL,
  `Suffix` VARCHAR(20) NULL,
  `OfficeKey` VARCHAR(150) NULL,
  `PoliticianKey` VARCHAR(50) NULL,
  `OfficeResolveStatus` VARCHAR(16) NULL,
  `PoliticianResolveStatus` VARCHAR(16) NULL,
  `ResolveReason` VARCHAR(100) NULL,
  PRIMARY KEY (`RunCandidateId`),
  KEY `ix_runcandidates_run` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `RunMeasures` (
  `RunMeasureId` INT NOT NULL AUTO_INCREMENT,
  `RunId` INT NOT NULL,
  `StateCode` VARCHAR(2) NOT NULL,
  `ElectionDate` DATE NOT NULL,
  `MeasureId` VARCHAR(100) NOT NULL DEFAULT '',
  `Title` TEXT NULL,
  `Summary` TEXT NULL,
  `FullTextUrl` TEXT NULL,
  `Jurisdiction` VARCHAR(255) NOT NULL DEFAULT '',
  `County` VARCHAR(255) NULL,
  `OcdDivisionId` VARCHAR(255) NULL,
  `SourceUrl` TEXT NULL,
  `IsStatewideProposed` TINYINT(1) NOT NULL DEFAULT 0,
  `ReferendumKey` VARCHAR(150) NULL,
  `ResolveStatus` VARCHAR(16) NULL,
  `ResolveReason` VARCHAR(100) NULL,
  PRIMARY KEY (`RunMeasureId`),
  KEY `ix_runmeasures_run` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- One county's ballot for one election, as the source published it. The entries overlap
-- RunCandidates and RunMeasures but are kept verbatim: they are what that county's voters
-- actually see, including local races the statewide feed does not attribute.
CREATE TABLE `RunCountyBallots` (
  `RunCountyBallotId` INT NOT NULL AUTO_INCREMENT,
  `RunId` INT NOT NULL,
  `StateCode` VARCHAR(2) NOT NULL,
  `County` VARCHAR(255) NOT NULL,
  `OcdDivisionId` VARCHAR(255) NULL,
  `SourceUrl` TEXT NULL,
  `CandidateCount` INT NOT NULL DEFAULT 0,
  `MeasureCount` INT NOT NULL DEFAULT 0,
  PRIMARY KEY (`RunCountyBallotId`),
  KEY `ix_countyballots_run` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `RunCountyBallotCandidates` (
  `Id` INT NOT NULL AUTO_INCREMENT,
  `RunCountyBallotId` INT NOT NULL,
  `RunId` INT NOT NULL,
  `Office` VARCHAR(255) NOT NULL DEFAULT '',
  `District` VARCHAR(255) NULL,
  `OcdDivisionId` VARCHAR(255) NULL,
  `CandidateName` VARCHAR(255) NOT NULL DEFAULT '',
  `Party` VARCHAR(100) NULL,
  `SourceOfficeId` VARCHAR(100) NULL,
  `SourceOfficeType` VARCHAR(100) NULL,
  `LocalJurisdiction` VARCHAR(255) NULL,
  PRIMARY KEY (`Id`),
  KEY `ix_cbcandidates_ballot` (`RunCountyBallotId`),
  KEY `ix_cbcandidates_run` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `RunCountyBallotMeasures` (
  `Id` INT NOT NULL AUTO_INCREMENT,
  `RunCountyBallotId` INT NOT NULL,
  `RunId` INT NOT NULL,
  `MeasureId` VARCHAR(100) NOT NULL DEFAULT '',
  `Title` TEXT NULL,
  `Summary` TEXT NULL,
  `Jurisdiction` VARCHAR(255) NOT NULL DEFAULT '',
  `OcdDivisionId` VARCHAR(255) NULL,
  PRIMARY KEY (`Id`),
  KEY `ix_cbmeasures_ballot` (`RunCountyBallotId`),
  KEY `ix_cbmeasures_run` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- State-level, belongs to no election.
CREATE TABLE `PassCountyDirectory` (
  `Id` INT NOT NULL AUTO_INCREMENT,
  `PassId` INT NOT NULL,
  `StateCode` VARCHAR(2) NOT NULL,
  `CountyName` VARCHAR(255) NOT NULL,
  `CountyFips` VARCHAR(5) NULL,
  `OcdDivisionId` VARCHAR(255) NULL,
  `ElectionsOfficeUrl` TEXT NULL,
  `Address` TEXT NULL,
  `Phone` VARCHAR(100) NULL,
  PRIMARY KEY (`Id`),
  KEY `ix_countydirectory_pass` (`PassId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Statewide measures with no election date yet (not certified to a ballot).
CREATE TABLE `PassProposedMeasures` (
  `Id` INT NOT NULL AUTO_INCREMENT,
  `PassId` INT NOT NULL,
  `StateCode` VARCHAR(2) NOT NULL,
  `MeasureId` VARCHAR(100) NOT NULL DEFAULT '',
  `Title` TEXT NULL,
  `Summary` TEXT NULL,
  `FullTextUrl` TEXT NULL,
  `Jurisdiction` VARCHAR(255) NOT NULL DEFAULT '',
  `County` VARCHAR(255) NULL,
  `OcdDivisionId` VARCHAR(255) NULL,
  `SourceUrl` TEXT NULL,
  PRIMARY KEY (`Id`),
  KEY `ix_proposedmeasures_pass` (`PassId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Rows the collector produced whose election could not be identified. Never dropped
-- silently: they are recorded here with the reason and counted on the pass.
CREATE TABLE `PassUnassignedRows` (
  `Id` INT NOT NULL AUTO_INCREMENT,
  `PassId` INT NOT NULL,
  `RowType` VARCHAR(16) NOT NULL,                   -- candidate | measure | county_ballot
  `ElectionDate` VARCHAR(30) NULL,
  `ElectionType` VARCHAR(50) NULL,
  `Reason` VARCHAR(100) NOT NULL,                   -- no_matching_election | ambiguous_election
  `RowJson` JSON NULL,
  PRIMARY KEY (`Id`),
  KEY `ix_unassigned_pass` (`PassId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
