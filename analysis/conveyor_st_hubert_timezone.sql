SELECT
    NOW() AS SERVER_NOW,
    UTC_TIMESTAMP() AS SERVER_UTC,
    @@system_time_zone AS SYSTEM_TIME_ZONE,
    @@session.time_zone AS SESSION_TIME_ZONE;
