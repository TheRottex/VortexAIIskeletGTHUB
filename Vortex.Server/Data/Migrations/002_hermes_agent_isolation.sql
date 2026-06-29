CREATE TABLE IF NOT EXISTS PlanAgentPolicies (
    Id TEXT PRIMARY KEY,
    PlanId TEXT NOT NULL UNIQUE,
    DailyAgentRunLimit INTEGER NOT NULL,
    ActiveScheduledTaskLimit INTEGER NOT NULL,
    PersistentMemoryLimit INTEGER NOT NULL,
    IsSubAgentEnabled INTEGER NOT NULL,
    IsTerminalEnabled INTEGER NOT NULL,
    IsSystemCommandEnabled INTEGER NOT NULL,
    MaxRunSeconds INTEGER NOT NULL,
    MaxConcurrentRuns INTEGER NOT NULL,
    FileAccessScope TEXT NOT NULL,
    FOREIGN KEY (PlanId) REFERENCES SubscriptionPlans(Id)
);

CREATE TABLE IF NOT EXISTS UserAgentProfiles (
    Id TEXT PRIMARY KEY,
    UserId TEXT NOT NULL UNIQUE,
    HermesProfileName TEXT NOT NULL UNIQUE,
    HermesHomePath TEXT NOT NULL,
    Status TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    LastStartedAt TEXT NULL,
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS AgentUsageCounters (
    Id TEXT PRIMARY KEY,
    UserId TEXT NOT NULL,
    Date TEXT NOT NULL,
    AgentRuns INTEGER NOT NULL,
    InputTokens INTEGER NOT NULL,
    OutputTokens INTEGER NOT NULL,
    EstimatedCost REAL NOT NULL,
    UpdatedAt TEXT NOT NULL,
    UNIQUE(UserId, Date),
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS AgentExecutionLogs (
    Id TEXT PRIMARY KEY,
    UserId TEXT NOT NULL,
    AgentProfileId TEXT NULL,
    RequestId TEXT NOT NULL,
    StartedAt TEXT NOT NULL,
    FinishedAt TEXT NULL,
    Status TEXT NOT NULL,
    ErrorCode TEXT NULL,
    Model TEXT NULL,
    WasLimitRejected INTEGER NOT NULL,
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY (AgentProfileId) REFERENCES UserAgentProfiles(Id)
);

CREATE TABLE IF NOT EXISTS AgentScheduledTasks (
    Id TEXT PRIMARY KEY,
    UserId TEXT NOT NULL,
    AgentProfileId TEXT NOT NULL,
    ExternalHermesTaskId TEXT NULL,
    Name TEXT NOT NULL,
    Schedule TEXT NOT NULL,
    TimeZone TEXT NOT NULL,
    IsEnabled INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
    FOREIGN KEY (AgentProfileId) REFERENCES UserAgentProfiles(Id) ON DELETE CASCADE
);
