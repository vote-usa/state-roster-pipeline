-- Smoke insert using VoteUSA key rules. Validates the reconstructed schema
-- accepts a roster-shaped row. Not production data.

INSERT INTO `States` (`StateCode`, `IsState`, `State`, `ShortName`)
VALUES ('CA', 1, 'California', 'Calif.');

INSERT INTO `Counties` (`StateCode`, `CountyCode`, `County`, `StateCountyCode`, `URL`, `Phone`)
VALUES ('CA', '001', 'Alameda', '001', 'https://www.acgov.org/rov/', '');

INSERT INTO `Parties` (`PartyKey`, `PartyCode`, `StateCode`, `PartyOrder`, `PartyName`, `IsPartyMajor`)
VALUES ('CAD', 'D', 'CA', 1, 'Democratic', 1);

INSERT INTO `Elections` (
  `ElectionKey`, `StateCode`, `CountyCode`, `LocalKey`, `ElectionDate`, `ElectionYYYYMMDD`,
  `ElectionType`, `NationalPartyCode`, `PartyCode`, `ElectionStatus`, `ElectionDesc`, `IsViewable`
) VALUES (
  'CA20261103GA', 'CA', '', '', '2026-11-03', '20261103',
  'G', 'A', '', '', 'November 3, 2026 General Election', 1
);

INSERT INTO `Offices` (
  `OfficeKey`, `StateCode`, `CountyCode`, `LocalKey`, `DistrictCode`,
  `OfficeLine1`, `OfficeLine2`, `OfficeLevel`, `Incumbents`, `ElectionPositions`
) VALUES (
  'CAGovernor', 'CA', '', '', '',
  'Governor', '', 4, 1, 1
);

INSERT INTO `Politicians` (
  `PoliticianKey`, `StateCode`, `FName`, `LName`, `PartyKey`, `EmailAddr`, `WebAddr`
) VALUES (
  'CANewsomGavin', 'CA', 'Gavin', 'Newsom', 'CAD', NULL, NULL
);

INSERT INTO `ElectionsOffices` (
  `ElectionKey`, `OfficeKey`, `ElectionKeyState`, `ElectionKeyFederal`,
  `StateCode`, `CountyCode`, `LocalKey`, `DistrictCode`, `OfficeLevel`
) VALUES (
  'CA20261103GA', 'CAGovernor', 'CA20261103GA', 'US20261103GA',
  'CA', '', '', '', 4
);

INSERT INTO `ElectionsPoliticians` (
  `ElectionKey`, `OfficeKey`, `PoliticianKey`, `ElectionKeyState`, `ElectionKeyFederal`,
  `StateCode`, `CountyCode`, `LocalKey`, `DistrictCode`, `IsIncumbent`
) VALUES (
  'CA20261103GA', 'CAGovernor', 'CANewsomGavin', 'CA20261103GA', 'US20261103GA',
  'CA', '', '', '', 1
);

INSERT INTO `Referendums` (
  `ElectionKey`, `ReferendumKey`, `ElectionKeyState`, `StateCode`,
  `ReferendumTitle`, `ReferendumDesc`, `ReferendumFullTextUrl`
) VALUES (
  'CA20261103GA', 'CA20261103GAProp1', 'CA20261103GA', 'CA',
  'Proposition 1', 'Example measure', 'https://sos.ca.gov/'
);

INSERT INTO `RosterProvenance` (`EntityType`, `EntityKey`, `SourceUrl`, `OcdDivisionId`)
VALUES (
  'election', 'CA20261103GA',
  'https://www.sos.ca.gov/elections/upcoming-elections',
  'ocd-division/country:us/state:ca'
);

-- Console sign-in users (VoteProject Security table; passwords are compared as stored).
INSERT INTO `Security` (`UserSecurity`, `UserName`, `UserPassword`, `UserEmail`, `UserStateCode`, `UserCountyCode`, `UserLocalKey`, `IsSuperUser`, `IsStaging`)
VALUES
  ('MASTER', 'master', 'master', 'master@example.com', '', '', '', 1, 0),
  ('ADMIN',  'wvadmin', 'wvadmin', 'wvadmin@example.com', 'WV', '', '', 0, 0);

-- One local jurisdiction so LocalKey lookups have something to hit.
INSERT INTO `LocalDistricts` (`StateCode`, `LocalKey`, `LocalDistrict`)
VALUES ('WV', 'HAMLN', 'Town of Hamlin');
