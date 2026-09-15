-- The mysql image grants the app user on MYSQL_DATABASE only. The staging schema is created by the pipeline migrator, so grant it up front.
GRANT ALL PRIVILEGES ON `roster_staging`.* TO 'roster'@'%';
FLUSH PRIVILEGES;
