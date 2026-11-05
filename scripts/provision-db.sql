-- Provisioning script for NovaFleetDb.
-- Secrets are passed as sqlcmd variables and never stored in this file.
IF DB_ID(N'$(DbName)') IS NULL
BEGIN
    CREATE DATABASE [$(DbName)];
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(AppLogin)')
BEGIN
    CREATE LOGIN [$(AppLogin)] WITH PASSWORD = N'$(AppPassword)', CHECK_POLICY = ON;
END;
GO

USE [$(DbName)];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(AppLogin)')
BEGIN
    CREATE USER [$(AppLogin)] FOR LOGIN [$(AppLogin)];
END;
GO

ALTER ROLE [db_owner] ADD MEMBER [$(AppLogin)];
GO

SELECT N'ready' AS status, DB_NAME() AS database_name;
GO
