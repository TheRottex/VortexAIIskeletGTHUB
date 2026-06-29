CREATE TABLE IF NOT EXISTS DesktopAuthSessions (
    Id TEXT PRIMARY KEY,
    StateHash TEXT NOT NULL,
    CodeChallenge TEXT NOT NULL,
    CallbackUri TEXT NOT NULL,
    UserId TEXT NULL,
    AuthorizationCodeHash TEXT NULL,
    ExpiresAt TEXT NOT NULL,
    CompletedAt TEXT NULL,
    ConsumedAt TEXT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_DesktopAuthSessions_StateHash ON DesktopAuthSessions(StateHash);
