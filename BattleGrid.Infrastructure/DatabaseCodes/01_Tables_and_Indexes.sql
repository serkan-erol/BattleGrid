--------------------------------------------------------------------------------------------------------------------------------
----------------------------------------------------------- *TABLES* -----------------------------------------------------------
--------------------------------------------------------------------------------------------------------------------------------

CREATE TABLE "User" (
    "UserID" SERIAL PRIMARY KEY,
    "UserName" VARCHAR(100) NOT NULL UNIQUE,
        -- Only a-z, A-Z, 0-9 and _ are allowed in UserName. No spaces or special characters.
        CONSTRAINT "Proper_UserName" CHECK ("UserName" ~* '^[A-Za-z0-9_]+$'),
    "Email" VARCHAR(100) NOT NULL UNIQUE 
        -- Basic email format validation:
        CONSTRAINT "Proper_Email" CHECK ("Email" ~* '^[A-Za-z0-9._+%-]+@[A-Za-z0-9.-]+[.][A-Za-z]+$'),
    -- We will store the password hash as a string. 
    -- The actual hashing will be done in the application layer using a strong hashing algorithm, BCrypt
    "PasswordHash" VARCHAR(255) NOT NULL,
    "IsAdmin" BOOLEAN NOT NULL DEFAULT FALSE,
    "IsBanned" BOOLEAN NOT NULL DEFAULT FALSE,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "LastUpdatedAt" TIMESTAMPTZ,
    "LastUserNameChangeAt" TIMESTAMPTZ,
    "LastEmailChangeAt" TIMESTAMPTZ,
    "UpdateReason" VARCHAR(255)
);

CREATE TABLE "PlayerStat" (
    "StatID" SERIAL PRIMARY KEY,
    "UserID" INT NOT NULL REFERENCES "User"("UserID"), -- FK to User table
    "SeasonNo" INT NOT NULL, -- To track stats per season
    "MatchesPlayed" INT NOT NULL DEFAULT 0,
    "MatchesWon" INT NOT NULL DEFAULT 0,
    "WinRate" DECIMAL(5, 2) GENERATED ALWAYS AS (
        CASE WHEN "MatchesPlayed" = 0 THEN CAST(0 AS DECIMAL(5, 2))
             ELSE CAST(("MatchesWon"::numeric * 100 / "MatchesPlayed"::numeric) AS DECIMAL(5, 2))
        END
    ) STORED CHECK ("WinRate" >= 0),
    "Rating" INT NOT NULL DEFAULT 1000 CHECK ("Rating" >= 400 AND "Rating" <= 3000), -- Starting rating for new players is 1000
    "HighestRating" INT NOT NULL DEFAULT 1000, -- Will be updated whenever CurrentRating exceeds it
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "LastUpdatedAt" TIMESTAMPTZ,
    UNIQUE ("UserID", "SeasonNo") -- Ensure one stats record per user per season
);

CREATE TABLE "Session" (
    "SessionID" SERIAL PRIMARY KEY,
    "UserID" INT NOT NULL REFERENCES "User"("UserID"),
    "RefreshToken" VARCHAR(255) NOT NULL UNIQUE,
    "RT_ExpiresAt" TIMESTAMPTZ NOT NULL,
    "AccessToken" VARCHAR(500) NOT NULL UNIQUE,
    "AT_ExpiresAt" TIMESTAMPTZ NOT NULL,
    "LastLogin" TIMESTAMPTZ NOT NULL,
    "IsRevoked" BOOLEAN NOT NULL DEFAULT FALSE,
    "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "LastUpdatedAt" TIMESTAMPTZ
);

CREATE TABLE "Match" (
    "MatchID" SERIAL PRIMARY KEY,
    "Player1ID" INT NOT NULL REFERENCES "User"("UserID"),
    "Player2ID" INT NOT NULL REFERENCES "User"("UserID"),
    "P1RatingChange" INT,
    "P2RatingChange" INT,
    -- 0 = Abondoned, 1 = P1 Won, 2 = P2 Won, 3 = In Progress, 4 = Placing Ships, 5 = Waiting/Loading
    "Status" INT NOT NULL DEFAULT '5'
        CHECK ("Status" BETWEEN 0 AND 5),
    "TotalNoOfMoves" INT NOT NULL DEFAULT 0,
    "FinishReason" VARCHAR(255),
    "StartedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "FinishedAt" TIMESTAMPTZ
);

CREATE TABLE "MatchMove" (
    "MoveID" SERIAL PRIMARY KEY,
    "PlayerID" INT NOT NULL REFERENCES "User"("UserID"),
    "MatchID" INT NOT NULL REFERENCES "Match"("MatchID"),
    "MoveNumber" INT NOT NULL,
    "HitX" INT NOT NULL,
    "HitY" INT NOT NULL,
    "IsHit" BOOLEAN NOT NULL,
    "TimeOfMove" TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE "ShipType" (
    "ShipID" SERIAL PRIMARY KEY,
    "ShipName" VARCHAR(50) NOT NULL UNIQUE,
    "Length" INT NOT NULL CHECK ("Length" > 0),
    "Width" INT NOT NULL CHECK ("Width" > 0),
    "MaxPerPlayer" INT NOT NULL CHECK ("MaxPerPlayer" > 0)
);

CREATE TABLE "ShipPlacement" (
    "PlacementID" SERIAL PRIMARY KEY,
    "PlayerID" INT NOT NULL REFERENCES "User"("UserID"),
    "MatchID" INT NOT NULL REFERENCES "Match"("MatchID"),
    "ShipID" INT NOT NULL REFERENCES "ShipType"("ShipID"),
    "StartX" INT NOT NULL,
    "StartY" INT NOT NULL,
    "IsVertical" BOOLEAN NOT NULL,
    "PlacedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE "Spectator" (
    "SpectatorID" INT NOT NULL REFERENCES "User"("UserID"),
    "MatchID" INT NOT NULL REFERENCES "Match"("MatchID"),
        PRIMARY KEY ("SpectatorID", "MatchID"),
        UNIQUE ("SpectatorID", "MatchID"),
    "JoinedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "Duration" INTERVAL DEFAULT '0 seconds'
);

CREATE TABLE "BanList" (
    "BanID" SERIAL PRIMARY KEY,
    "AdminID" INT NOT NULL REFERENCES "User"("UserID"),
    "PlayerID" INT NOT NULL REFERENCES "User"("UserID"),
    "BanReason" VARCHAR(255) NOT NULL,
    "IsReverted" BOOLEAN NOT NULL DEFAULT FALSE,
    -- NULL when the background worker auto-reverts an expired temporary ban; otherwise the admin UserID.
    "RevertingAdminID" INT REFERENCES "User"("UserID"),
    "RevertingReason" VARCHAR(255),
    "IsTemporary" BOOLEAN NOT NULL DEFAULT FALSE,
    "BannedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "Duration" INTERVAL, -- Only applicable for Temporary bans
    "BannedUntil" TIMESTAMPTZ -- Calculated based on Duration for Temporary bans
);

--------------------------------------------------------------------------------------------------------------------------------
--------------------------------------------------------- *INSERTIONS* ---------------------------------------------------------
--------------------------------------------------------------------------------------------------------------------------------

INSERT INTO "ShipType" ("ShipName", "Length", "Width", "MaxPerPlayer") VALUES
('Carrier',    4, 1, 1),
('Battleship', 5, 1, 1),
('Cruiser',    3, 1, 2),
('Submarine',  3, 1, 2),
('Destroyer',  2, 1, 2);

INSERT INTO "User" ("UserName", "Email", "PasswordHash", "IsAdmin") VALUES
('P1', 'p1@test.com', '1234', 'true'),
('P2', 'p2@test.com', '1234', 'false'),
('P3', 'p3@test.com', '1234', 'false'),
('P4', 'p4@test.com', '1234', 'false');

--------------------------------------------------------------------------------------------------------------------------------
---------------------------------------------------------- *INDEXES* -----------------------------------------------------------
--------------------------------------------------------------------------------------------------------------------------------

CREATE INDEX idx_user_username ON "User" ("UserName");
CREATE INDEX idx_user_email ON "User" ("Email");

CREATE INDEX idx_playerstat_matchesplayed ON "PlayerStat" ("MatchesPlayed");
CREATE INDEX idx_playerstat_matcheswon ON "PlayerStat" ("MatchesWon");
CREATE INDEX idx_playerstat_winrate ON "PlayerStat" ("WinRate");
CREATE INDEX idx_playerstat_rating ON "PlayerStat" ("Rating");
CREATE INDEX idx_playerstat_highestrating ON "PlayerStat" ("HighestRating");

CREATE INDEX idx_session_refreshtoken ON "Session" ("RefreshToken");
CREATE INDEX idx_session_rt_expiresat ON "Session" ("RT_ExpiresAt");
CREATE INDEX idx_session_accesstoken ON "Session" ("AccessToken");
CREATE INDEX idx_session_at_expiresat ON "Session" ("AT_ExpiresAt");

CREATE INDEX idx_match_Player1ID ON "Match" ("Player1ID");
CREATE INDEX idx_match_Player2ID ON "Match" ("Player2ID");
CREATE INDEX idx_match_StartedAt ON "Match" ("StartedAt");

CREATE INDEX idx_matchmove_PlayerID ON "MatchMove" ("PlayerID");
CREATE INDEX idx_matchmove_MatchID ON "MatchMove" ("MatchID");

CREATE INDEX idx_spectator_spectatorid_matchid ON "Spectator" ("SpectatorID", "MatchID");

CREATE INDEX idx_banlist_adminid ON "BanList" ("AdminID");
CREATE INDEX idx_banlist_playerid ON "BanList" ("PlayerID");
CREATE INDEX idx_banlist_revertingadminid ON "BanList" ("RevertingAdminID");
CREATE INDEX idx_banlist_bannedat ON "BanList" ("BannedAt");
CREATE INDEX idx_banlist_duration ON "BanList" ("Duration");
CREATE INDEX idx_banlist_banneduntil ON "BanList" ("BannedUntil");
CREATE INDEX idx_banlist_istemporary_bannedat ON "BanList" ("IsTemporary", "BannedAt");
CREATE INDEX idx_banlist_istemporary_duration ON "BanList" ("IsTemporary", "Duration");
CREATE INDEX idx_banlist_istemporary_banneduntil ON "BanList" ("IsTemporary", "BannedUntil");
CREATE INDEX idx_banlist_istemporary_bannedat_duration ON "BanList" ("IsTemporary", "BannedAt", "Duration");