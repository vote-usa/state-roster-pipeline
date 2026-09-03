-- Immutable collector runs. One row per run; child rows mirror the JSON/CSV
-- output rows (ResultWriter DTOs) plus resolution columns filled later.
-- Applied by StateBallot.Staging.Migrator inside the roster_staging schema.

CREATE TABLE `Runs` (
  `RunId` INT NOT NULL AUTO_INCREMENT,
  `StateCode` VARCHAR(2) NOT NULL,
  `Year` INT NOT NULL,
  `Status` VARCHAR(16) NOT NULL DEFAULT 'queued',   -- queued | running | succeeded | failed
  `Source` VARCHAR(8) NOT NULL DEFAULT 'cli',       -- cli | web
  `RequestedBy` VARCHAR(100) NOT NULL DEFAULT '',
  `RequestedAt` DATETIME NOT NULL,
  `StartedAt` DATETIME NULL,
  `FinishedAt` DATETIME NULL,
  `CancelRequested` TINYINT(1) NOT NULL DEFAULT 0,
  `CliArgs` TEXT NULL,
  `GitSha` VARCHAR(40) NULL,
  `Wayback` VARCHAR(14) NULL,
  `ElectionCount` INT NOT NULL DEFAULT 0,
  `PendingElectionCount` INT NOT NULL DEFAULT 0,
  `CandidateCount` INT NOT NULL DEFAULT 0,
  `MeasureCount` INT NOT NULL DEFAULT 0,
  `CountyBallotCount` INT NOT NULL DEFAULT 0,
  `GapCount` INT NOT NULL DEFAULT 0,
  `GapsJson` JSON NULL,
  `Summary` TEXT NULL,
  `LogText` MEDIUMTEXT NULL,
  `ErrorText` TEXT NULL,
  PRIMARY KEY (`RunId`),
  KEY `ix_runs_state_requested` (`StateCode`, `RequestedAt`),
  KEY `ix_runs_status` (`Status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `RunElections` (
  `RunElectionId` INT NOT NULL AUTO_INCREMENT,
  `RunId` INT NOT NULL,
  `StateCode` VARCHAR(2) NOT NULL,
  `ElectionDate` DATE NOT NULL,
  `ElectionType` VARCHAR(50) NOT NULL DEFAULT '',
  `Jurisdiction` VARCHAR(200) NOT NULL DEFAULT '',
  `OcdDivisionId` VARCHAR(255) NULL,
  `Name` VARCHAR(255) NOT NULL DEFAULT '',
  `SourceElectionId` VARCHAR(50) NOT NULL DEFAULT '',
  `SourceUrl` TEXT NULL,
  `IsPending` TINYINT(1) NOT NULL DEFAULT 0,       -- source has not published ballot data yet
  `ElectionKey` VARCHAR(18) NULL,                  -- VoteUSA key once resolved
  `ResolveStatus` VARCHAR(16) NULL,                -- resolved | new | unresolved
  `ResolveReason` VARCHAR(100) NULL,
  PRIMARY KEY (`RunElectionId`),
  KEY `ix_runelections_run` (`RunId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `RunCandidates` (
  `RunCandidateId` INT NOT NULL AUTO_INCREMENT,
  `RunId` INT NOT NULL,
  `RunElectionId` INT NULL,
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
  `OfficeKey` VARCHAR(150) NULL,                   -- VoteUSA key once resolved
  `PoliticianKey` VARCHAR(50) NULL,
  `OfficeResolveStatus` VARCHAR(16) NULL,          -- resolved | new | unresolved
  `PoliticianResolveStatus` VARCHAR(16) NULL,
  `ResolveReason` VARCHAR(100) NULL,
  PRIMARY KEY (`RunCandidateId`),
  KEY `ix_runcandidates_run` (`RunId`),
  KEY `ix_runcandidates_election` (`RunId`, `RunElectionId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `RunMeasures` (
  `RunMeasureId` INT NOT NULL AUTO_INCREMENT,
  `RunId` INT NOT NULL,
  `RunElectionId` INT NULL,
  `StateCode` VARCHAR(2) NOT NULL,
  `ElectionDate` DATE NULL,                        -- null until a statewide measure is certified to a ballot
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
