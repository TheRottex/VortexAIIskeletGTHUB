-- Vortex initial SQLite schema is applied by Vortex.Server/Data/VortexDb.cs during startup.
-- This file documents the first migration boundary for later EF Core or DbUp migration adoption.

CREATE TABLE IF NOT EXISTS SubscriptionPlans (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL UNIQUE,
    DisplayName TEXT NOT NULL,
    StorageQuotaBytes INTEGER NOT NULL,
    DailyRequestLimit INTEGER NOT NULL,
    MonthlyRequestLimit INTEGER NOT NULL,
    IsActive INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS Users (
    Id TEXT PRIMARY KEY,
    Email TEXT NOT NULL UNIQUE,
    DisplayName TEXT NOT NULL,
    PasswordHash TEXT NOT NULL,
    PasswordSalt TEXT NOT NULL,
    Role TEXT NOT NULL,
    PlanId TEXT NOT NULL,
    StorageUsedBytes INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (PlanId) REFERENCES SubscriptionPlans(Id)
);
