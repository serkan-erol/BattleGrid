--------------------------------------------------------------------------------------------------------------------------------
------------------------------------------------ *Stored Procedures & Triggers* ------------------------------------------------
--------------------------------------------------------------------------------------------------------------------------------

-- Create a trigger function to validate ship placement
CREATE OR REPLACE FUNCTION validate_ship_placement() RETURNS TRIGGER AS $$
    BEGIN
        -- Bounds: coordinates are 0..9 with origin bottom-left (same as app). Extent uses Length on primary axis and Width on secondary (matches ShipPlacementHelper).
        IF NEW."StartX" < 0 OR NEW."StartY" < 0 THEN
            RAISE EXCEPTION 'Ship placement is out of bounds';
        END IF;

        IF NEW."IsVertical" THEN
            IF NEW."StartX" + (SELECT "Width" FROM "ShipType" WHERE "ShipID" = NEW."ShipID") > 10
               OR NEW."StartY" + (SELECT "Length" FROM "ShipType" WHERE "ShipID" = NEW."ShipID") > 10 THEN
                RAISE EXCEPTION 'Ship placement is out of bounds';
            END IF;
        ELSE
            IF NEW."StartX" + (SELECT "Length" FROM "ShipType" WHERE "ShipID" = NEW."ShipID") > 10
               OR NEW."StartY" + (SELECT "Width" FROM "ShipType" WHERE "ShipID" = NEW."ShipID") > 10 THEN
                RAISE EXCEPTION 'Ship placement is out of bounds';
            END IF;
        END IF;

        -- Validate no overlap with existing placements
        PERFORM 1
        FROM "ShipPlacement"
        WHERE "PlayerID" = NEW."PlayerID"
          AND "MatchID" = NEW."MatchID"
          AND (
              (NEW."IsVertical" AND "IsVertical" 
              AND NEW."StartX" = "StartX" 
              AND NEW."StartY" < "StartY" + (SELECT "Length" FROM "ShipType" WHERE "ShipID" = "ShipPlacement"."ShipID") 
              AND NEW."StartY" + (SELECT "Length" FROM "ShipType" WHERE "ShipID" = NEW."ShipID") > "StartY")
              OR
              (NOT NEW."IsVertical" AND NOT "IsVertical" 
              AND NEW."StartY" = "StartY" 
              AND NEW."StartX" < "StartX" + (SELECT "Length" FROM "ShipType" WHERE "ShipID" = "ShipPlacement"."ShipID") 
              AND NEW."StartX" + (SELECT "Length" FROM "ShipType" WHERE "ShipID" = NEW."ShipID") > "StartX")
              OR
              (NEW."IsVertical" AND NOT "IsVertical" 
              AND NEW."StartX" >= "StartX" 
              AND NEW."StartX" < "StartX" + (SELECT "Length" FROM "ShipType" WHERE "ShipID" = "ShipPlacement"."ShipID") 
              AND NEW."StartY" + (SELECT "Length" FROM "ShipType" WHERE "ShipID" = NEW."ShipID") > "StartY" 
              AND NEW."StartY" <= "StartY")
              OR
              (NOT NEW."IsVertical" AND "IsVertical" 
              AND NEW."StartY" >= "StartY" 
              AND NEW."StartY" < "StartY" + (SELECT "Length" FROM "ShipType" WHERE "ShipID" = "ShipPlacement"."ShipID") 
              AND NEW."StartX" + (SELECT "Length" FROM "ShipType" WHERE "ShipID" = NEW."ShipID") > "StartX" 
              AND NEW."StartX" <= "StartX")
          );

        IF FOUND THEN
            RAISE EXCEPTION 'Ship placement overlaps with an existing ship';
        END IF;

        RETURN NEW;
    END;

$$ LANGUAGE plpgsql;

-- Add the trigger to the "ShipPlacement" table
CREATE TRIGGER validate_ship_placement_trigger
    BEFORE INSERT OR UPDATE ON "ShipPlacement"
    FOR EACH ROW
    EXECUTE FUNCTION validate_ship_placement();

--------------------------------------------------------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION validate_ship_count() RETURNS TRIGGER AS $$
    -- Validate if the user reached the max limit of a certain ship type
    DECLARE placed_count INT;
    DECLARE max_allowed INT;

    BEGIN
        SELECT COUNT(*) INTO placed_count
        FROM "ShipPlacement"
        WHERE "PlayerID" = NEW."PlayerID"
          AND "MatchID" = NEW."MatchID"
          AND "ShipID" = NEW."ShipID";

        SELECT "MaxPerPlayer" INTO  max_allowed
        FROM "ShipType"
        WHERE "ShipID" = NEW."ShipID";

        IF placed_count >= max_allowed THEN
            RAISE EXCEPTION 'Player has already placed the maximum number of this ship type';
        END IF;

        RETURN NEW;
    END;

$$ LANGUAGE plpgsql;

-- Add the trigger to the "ShipPlacement" table
CREATE TRIGGER validate_ship_count_trigger
    BEFORE INSERT OR UPDATE ON "ShipPlacement"
    FOR EACH ROW
    EXECUTE FUNCTION validate_ship_count();

--------------------------------------------------------------------------------------------------------------------------------

--aaa TODO: Implement a SP and a trigger that prevents any updates to it, 
--         after a BanList entry is already marked as reverted