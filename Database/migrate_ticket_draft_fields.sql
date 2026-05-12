/*
  Migration: Add extended draft and test fields to ProjectTickets
  
  This script adds columns required for AI-generated draft tickets, including
  structured test fields, build validation, and generation metadata.
*/

USE [IronDeveloper];
GO

IF COL_LENGTH('dbo.ProjectTickets', 'UnitTests') IS NULL
BEGIN
    PRINT 'Adding extended fields to dbo.ProjectTickets...';
    
    ALTER TABLE dbo.ProjectTickets ADD UnitTests NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD IntegrationTests NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD ManualTests NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD RegressionTests NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD BuildValidation NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD ContextSummary NVARCHAR(MAX) NULL;
    
    ALTER TABLE dbo.ProjectTickets ADD IsGenerated BIT NOT NULL 
        CONSTRAINT DF_ProjectTickets_IsGenerated DEFAULT 0;
        
    ALTER TABLE dbo.ProjectTickets ADD GenerationNote NVARCHAR(MAX) NULL;
    
    PRINT 'Extended fields added successfully.';
END
ELSE
BEGIN
    PRINT 'Extended fields already exist in dbo.ProjectTickets.';
END
GO
