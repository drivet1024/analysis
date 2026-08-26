using MySqlConnector;
using System.Text.Json;

const string defaultHost = "192.168.1.222";
const uint defaultPort = 3306;
const string defaultDatabase = "nationex";
const string defaultUser = "user_ro";

var password = LoadPassword();
if (string.IsNullOrWhiteSpace(password))
{
    Console.Error.WriteLine(
        "Mot de passe manquant. Définis MYSQL_PASSWORD ou ajoute-le dans MySqlTool/.env.local.");
    return 2;
}

var port = uint.TryParse(Environment.GetEnvironmentVariable("MYSQL_PORT"), out var configuredPort)
    ? configuredPort
    : defaultPort;

var connectionString = new MySqlConnectionStringBuilder
{
    Server = Environment.GetEnvironmentVariable("MYSQL_HOST") ?? defaultHost,
    Port = port,
    Database = Environment.GetEnvironmentVariable("MYSQL_DATABASE") ?? defaultDatabase,
    UserID = Environment.GetEnvironmentVariable("MYSQL_USER") ?? defaultUser,
    Password = password,
    ConnectionTimeout = 5,
    DefaultCommandTimeout = uint.TryParse(
        Environment.GetEnvironmentVariable("MYSQL_COMMAND_TIMEOUT"),
        out var configuredCommandTimeout)
        ? configuredCommandTimeout
        : 30,
    SslMode = MySqlSslMode.None,
    AllowPublicKeyRetrieval = true,
}.ConnectionString;

try
{
    await using var connection = new MySqlConnection(connectionString);
    await connection.OpenAsync();

    var sqlJsonIndex = Array.FindIndex(
        args,
        argument => argument.Equals("--sql-json", StringComparison.OrdinalIgnoreCase));
    if (sqlJsonIndex >= 0)
    {
        if (sqlJsonIndex + 2 >= args.Length)
        {
            Console.Error.WriteLine("Utilisation : --sql-json <requête.sql> <résultat.json>.");
            return 2;
        }

        var sql = await File.ReadAllTextAsync(args[sqlJsonIndex + 1]);
        if (!IsReadOnlySql(sql))
        {
            Console.Error.WriteLine("Seules les requêtes de lecture sont acceptées avec --sql-json.");
            return 2;
        }

        await WriteRowsAsJson(connection, sql, args[sqlJsonIndex + 2]);
        return 0;
    }

    var sqlFileIndex = Array.FindIndex(
        args,
        argument => argument.Equals("--sql-file", StringComparison.OrdinalIgnoreCase));
    if (sqlFileIndex >= 0)
    {
        if (sqlFileIndex + 1 >= args.Length)
        {
            Console.Error.WriteLine("Chemin manquant après --sql-file.");
            return 2;
        }

        var sql = await File.ReadAllTextAsync(args[sqlFileIndex + 1]);
        if (!IsReadOnlySql(sql))
        {
            Console.Error.WriteLine("Seules les requêtes de lecture sont acceptées avec --sql-file.");
            return 2;
        }

        await PrintRows(connection, sql);
        return 0;
    }

    if (args.Contains("--schema-summary", StringComparer.OrdinalIgnoreCase))
    {
        await PrintSchemaSummary(connection);
        return 0;
    }

    if (args.Contains("--schema-details", StringComparer.OrdinalIgnoreCase))
    {
        await PrintSchemaDetails(connection);
        return 0;
    }

    if (args.Contains("--schema-physical", StringComparer.OrdinalIgnoreCase))
    {
        await PrintSchemaPhysical(connection);
        return 0;
    }

    if (args.Contains("--grants", StringComparer.OrdinalIgnoreCase))
    {
        await PrintRows(connection, "SHOW GRANTS FOR CURRENT_USER()");
        return 0;
    }

    if (args.Contains("--schema-deep", StringComparer.OrdinalIgnoreCase))
    {
        await PrintSchemaDeep(connection);
        return 0;
    }

    if (args.Contains("--schema-families", StringComparer.OrdinalIgnoreCase))
    {
        await PrintSchemaFamilies(connection);
        return 0;
    }

    if (args.Contains("--verify-keys", StringComparer.OrdinalIgnoreCase))
    {
        await VerifyKeyMetadata(connection);
        return 0;
    }

    if (args.Contains("--integrity-explain", StringComparer.OrdinalIgnoreCase))
    {
        await ExplainIntegrityCheck(connection);
        return 0;
    }

    if (args.Contains("--integrity-check", StringComparer.OrdinalIgnoreCase))
    {
        await RunIntegrityCheck(connection);
        return 0;
    }

    if (args.Contains("--integrity-candidates-explain", StringComparer.OrdinalIgnoreCase))
    {
        await InvestigateShipmentKey(connection, explainOnly: true);
        return 0;
    }

    if (args.Contains("--integrity-candidates", StringComparer.OrdinalIgnoreCase))
    {
        await InvestigateShipmentKey(connection, explainOnly: false);
        return 0;
    }

    if (args.Contains("--integrity-corrected-explain", StringComparer.OrdinalIgnoreCase))
    {
        await RunCorrectedIntegrityCheck(connection, explainOnly: true);
        return 0;
    }

    if (args.Contains("--integrity-corrected", StringComparer.OrdinalIgnoreCase))
    {
        await RunCorrectedIntegrityCheck(connection, explainOnly: false);
        return 0;
    }

    if (args.Contains("--integrity-history-segments", StringComparer.OrdinalIgnoreCase))
    {
        await SegmentHistoryIntegrity(connection);
        return 0;
    }

    if (args.Contains("--source-type-metadata", StringComparer.OrdinalIgnoreCase))
    {
        await InspectSourceTypeMetadata(connection);
        return 0;
    }

    if (args.Contains("--source-type-600", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine("=== REFERENCE SOURCE_TYPE 600 ===");
        await PrintRows(connection, "SELECT * FROM parcel_history_source_type WHERE SOURCE_TYPE = 600");
        return 0;
    }

    if (args.Contains("--source-type-anomalies", StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine("=== REFERENCES SOURCE_TYPE 200 ET 1400 ===");
        await PrintRows(
            connection,
            "SELECT * FROM parcel_history_source_type WHERE SOURCE_TYPE IN (200, 1400) ORDER BY SOURCE_TYPE");
        return 0;
    }

    if (args.Contains("--source-type-coverage-explain", StringComparer.OrdinalIgnoreCase))
    {
        await InspectSourceTypeCoverage(connection, explainOnly: true);
        return 0;
    }

    if (args.Contains("--source-type-coverage", StringComparer.OrdinalIgnoreCase))
    {
        await InspectSourceTypeCoverage(connection, explainOnly: false);
        return 0;
    }

    if (args.Contains("--integrity-natclik", StringComparer.OrdinalIgnoreCase))
    {
        await InspectNatClikIntegrity(connection, explainOnly: false);
        return 0;
    }

    if (args.Contains("--integrity-natclik-explain", StringComparer.OrdinalIgnoreCase))
    {
        await InspectNatClikIntegrity(connection, explainOnly: true);
        return 0;
    }

    if (args.Contains("--integrity-natclik-full-explain", StringComparer.OrdinalIgnoreCase))
    {
        await InspectNatClikFullDay(connection, explainOnly: true);
        return 0;
    }

    if (args.Contains("--integrity-natclik-full", StringComparer.OrdinalIgnoreCase))
    {
        await InspectNatClikFullDay(connection, explainOnly: false);
        return 0;
    }

    await using var command = connection.CreateCommand();
    command.CommandText =
        "SELECT VERSION(), DATABASE(), CURRENT_USER(), @@hostname";

    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();

    Console.WriteLine("Connexion MySQL réussie.");
    Console.WriteLine($"Serveur : {reader.GetString(3)}");
    Console.WriteLine($"Version : {reader.GetString(0)}");
    Console.WriteLine($"Base : {reader.GetString(1)}");
    Console.WriteLine($"Compte : {reader.GetString(2)}");
    return 0;
}
catch (MySqlException exception)
{
    Console.Error.WriteLine(
        $"Connexion MySQL échouée (erreur {exception.Number}) : {exception.Message}");
    return 1;
}

static bool IsReadOnlySql(string sql)
{
    var normalizedSql = sql.TrimStart();
    var readOnlyPrefixes = new[] { "SELECT", "SHOW", "DESCRIBE", "DESC", "EXPLAIN", "WITH" };
    return readOnlyPrefixes.Any(
        prefix => normalizedSql.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}

static async Task WriteRowsAsJson(MySqlConnection connection, string sql, string outputPath)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;

    await using var reader = await command.ExecuteReaderAsync();
    var rows = new List<Dictionary<string, object?>>();
    while (await reader.ReadAsync())
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < reader.FieldCount; index++)
        {
            row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
        }
        rows.Add(row);
    }

    await File.WriteAllTextAsync(
        outputPath,
        JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"{rows.Count} ligne(s) écrite(s) dans {outputPath}");
}

static string? LoadPassword()
{
    var environmentPassword = Environment.GetEnvironmentVariable("MYSQL_PASSWORD");
    if (!string.IsNullOrWhiteSpace(environmentPassword))
    {
        return environmentPassword;
    }

    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "MySqlTool", ".env.local"),
        Path.Combine(Directory.GetCurrentDirectory(), ".env.local"),
    };

    foreach (var path in candidates.Where(File.Exists))
    {
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            if (!key.Equals("MYSQL_PASSWORD", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return line[(separator + 1)..].Trim().Trim('"', '\'');
        }
    }

    return null;
}

static async Task PrintSchemaSummary(MySqlConnection connection)
{
    Console.WriteLine("=== OBJETS ===");
    await PrintRows(
        connection,
        """
        SELECT TABLE_TYPE AS type_objet,
               COUNT(*) AS nombre
        FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE()
        GROUP BY TABLE_TYPE
        ORDER BY TABLE_TYPE
        """);

    Console.WriteLine("\n=== STRUCTURE ===");
    await PrintRows(
        connection,
        """
        SELECT
          (SELECT COUNT(*)
             FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()) AS colonnes,
          (SELECT COUNT(DISTINCT TABLE_NAME)
             FROM information_schema.TABLE_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE()
              AND CONSTRAINT_TYPE = 'PRIMARY KEY') AS tables_avec_cle_primaire,
          (SELECT COUNT(*)
             FROM information_schema.REFERENTIAL_CONSTRAINTS
            WHERE CONSTRAINT_SCHEMA = DATABASE()) AS cles_etrangeres,
          (SELECT COUNT(DISTINCT INDEX_NAME)
             FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE()) AS index_nommes,
          (SELECT COUNT(*)
             FROM information_schema.ROUTINES
            WHERE ROUTINE_SCHEMA = DATABASE()) AS routines,
          (SELECT COUNT(*)
             FROM information_schema.TRIGGERS
            WHERE TRIGGER_SCHEMA = DATABASE()) AS declencheurs
        """);

    Console.WriteLine("\n=== PREFIXES DE TABLES ===");
    await PrintRows(
        connection,
        """
        SELECT SUBSTRING_INDEX(TABLE_NAME, '_', 1) AS prefixe,
               COUNT(*) AS tables
        FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE()
        GROUP BY prefixe
        ORDER BY tables DESC, prefixe
        LIMIT 50
        """);

    Console.WriteLine("\n=== PLUS GRANDES TABLES (ESTIMATION INNODB) ===");
    await PrintRows(
        connection,
        """
        SELECT TABLE_NAME AS table_nom,
               TABLE_ROWS AS lignes_estimees,
               ROUND((DATA_LENGTH + INDEX_LENGTH) / 1024 / 1024, 2) AS taille_mib
        FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_TYPE = 'BASE TABLE'
        ORDER BY (DATA_LENGTH + INDEX_LENGTH) DESC, TABLE_NAME
        LIMIT 30
        """);

    Console.WriteLine("\n=== TABLES LES PLUS RELIEES ===");
    await PrintRows(
        connection,
        """
        SELECT table_nom,
               SUM(sortantes) AS relations_sortantes,
               SUM(entrantes) AS relations_entrantes
        FROM (
          SELECT TABLE_NAME AS table_nom, COUNT(*) AS sortantes, 0 AS entrantes
          FROM information_schema.KEY_COLUMN_USAGE
          WHERE TABLE_SCHEMA = DATABASE()
            AND REFERENCED_TABLE_NAME IS NOT NULL
          GROUP BY TABLE_NAME
          UNION ALL
          SELECT REFERENCED_TABLE_NAME AS table_nom, 0 AS sortantes, COUNT(*) AS entrantes
          FROM information_schema.KEY_COLUMN_USAGE
          WHERE TABLE_SCHEMA = DATABASE()
            AND REFERENCED_TABLE_NAME IS NOT NULL
          GROUP BY REFERENCED_TABLE_NAME
        ) AS relations
        GROUP BY table_nom
        ORDER BY (SUM(sortantes) + SUM(entrantes)) DESC, table_nom
        LIMIT 30
        """);
}

static async Task PrintSchemaDetails(MySqlConnection connection)
{
    Console.WriteLine("=== VUES ===");
    await PrintRows(
        connection,
        """
        SELECT TABLE_NAME AS vue
        FROM information_schema.VIEWS
        WHERE TABLE_SCHEMA = DATABASE()
        ORDER BY TABLE_NAME
        """);

    Console.WriteLine("\n=== RELATIONS EXPLICITES ===");
    await PrintRows(
        connection,
        """
        SELECT TABLE_NAME AS table_source,
               COLUMN_NAME AS colonne_source,
               REFERENCED_TABLE_NAME AS table_cible,
               REFERENCED_COLUMN_NAME AS colonne_cible,
               CONSTRAINT_NAME AS contrainte
        FROM information_schema.KEY_COLUMN_USAGE
        WHERE TABLE_SCHEMA = DATABASE()
          AND REFERENCED_TABLE_NAME IS NOT NULL
        ORDER BY TABLE_NAME, ORDINAL_POSITION
        """);

    Console.WriteLine("\n=== ENTITES CENTRALES : RESUME DES COLONNES ===");
    await PrintRows(
        connection,
        """
        SELECT TABLE_NAME AS table_nom,
               COUNT(*) AS colonnes,
               GROUP_CONCAT(CASE WHEN COLUMN_KEY = 'PRI' THEN COLUMN_NAME END
                            ORDER BY ORDINAL_POSITION SEPARATOR ', ') AS cle_primaire,
               GROUP_CONCAT(CASE WHEN COLUMN_KEY IN ('PRI', 'UNI', 'MUL') THEN COLUMN_NAME END
                            ORDER BY ORDINAL_POSITION SEPARATOR ', ') AS colonnes_indexees
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME IN (
            'customer', 'shipment', 'parcel', 'parcel_history', 'livraison',
            'shipping', 'billing_invoice', 'live_route', 'depot', 'location', 'sac'
          )
        GROUP BY TABLE_NAME
        ORDER BY TABLE_NAME
        """);

    Console.WriteLine("\n=== COLONNES : CUSTOMER, SHIPMENT, PARCEL ===");
    await PrintRows(
        connection,
        """
        SELECT TABLE_NAME AS table_nom,
               COLUMN_NAME AS colonne,
               COLUMN_TYPE AS type,
               IS_NULLABLE AS nullable,
               COLUMN_KEY AS cle,
               EXTRA AS extra
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME IN ('customer', 'shipment', 'parcel')
        ORDER BY TABLE_NAME, ORDINAL_POSITION
        """);
}

static async Task PrintSchemaPhysical(MySqlConnection connection)
{
    Console.WriteLine("=== DROITS DU COMPTE ===");
    await PrintRows(connection, "SHOW GRANTS FOR CURRENT_USER()");

    Console.WriteLine("\n=== STOCKAGE ET PARTITIONNEMENT ===");
    await PrintRows(
        connection,
        """
        SELECT t.TABLE_NAME AS table_nom,
               t.ENGINE AS moteur,
               t.CREATE_OPTIONS AS options_creation,
               COUNT(DISTINCT p.PARTITION_NAME) AS partitions,
               GROUP_CONCAT(DISTINCT p.PARTITION_METHOD ORDER BY p.PARTITION_METHOD) AS methode_partition
        FROM information_schema.TABLES AS t
        LEFT JOIN information_schema.PARTITIONS AS p
          ON p.TABLE_SCHEMA = t.TABLE_SCHEMA
         AND p.TABLE_NAME = t.TABLE_NAME
        WHERE t.TABLE_SCHEMA = DATABASE()
          AND t.TABLE_NAME IN (
            'customer', 'shipment', 'parcel', 'parcel_history', 'livraison',
            'shipping', 'billing_invoice', 'live_route', 'depot', 'location', 'sac'
          )
        GROUP BY t.TABLE_NAME, t.ENGINE, t.CREATE_OPTIONS
        ORDER BY t.TABLE_NAME
        """);

    Console.WriteLine("\n=== INDEX DES ENTITES CENTRALES ===");
    await PrintRows(
        connection,
        """
        SELECT TABLE_NAME AS table_nom,
               INDEX_NAME AS index_nom,
               CASE NON_UNIQUE WHEN 0 THEN 'unique' ELSE 'non_unique' END AS unicite,
               GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX SEPARATOR ', ') AS colonnes
        FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME IN (
            'customer', 'shipment', 'parcel', 'parcel_history', 'livraison',
            'shipping', 'billing_invoice', 'live_route', 'depot', 'location', 'sac'
          )
        GROUP BY TABLE_NAME, INDEX_NAME, NON_UNIQUE
        ORDER BY TABLE_NAME, INDEX_NAME
        """);
}

static async Task PrintSchemaDeep(MySqlConnection connection)
{
    Console.WriteLine("=== FAMILLES PAR PREFIXE ET EMPREINTE ===");
    await PrintRows(
        connection,
        """
        SELECT COALESCE(
                 NULLIF(SUBSTRING_INDEX(TRIM(LEADING '_' FROM TABLE_NAME), '_', 1), ''),
                 '(sans_prefixe)'
               ) AS prefixe,
               COUNT(*) AS objets,
               SUM(TABLE_TYPE = 'VIEW') AS vues,
               SUM(COALESCE(TABLE_ROWS, 0)) AS lignes_estimees,
               ROUND(SUM(COALESCE(DATA_LENGTH, 0) + COALESCE(INDEX_LENGTH, 0)) / 1024 / 1024 / 1024, 2) AS taille_gib
        FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE()
        GROUP BY prefixe
        ORDER BY SUM(COALESCE(DATA_LENGTH, 0) + COALESCE(INDEX_LENGTH, 0)) DESC,
                 objets DESC,
                 prefixe
        LIMIT 60
        """);

    Console.WriteLine("\n=== TABLES SANS CLE PRIMAIRE ===");
    await PrintRows(
        connection,
        """
        SELECT t.TABLE_NAME AS table_nom,
               t.TABLE_ROWS AS lignes_estimees,
               ROUND((COALESCE(t.DATA_LENGTH, 0) + COALESCE(t.INDEX_LENGTH, 0)) / 1024 / 1024, 2) AS taille_mib
        FROM information_schema.TABLES AS t
        LEFT JOIN information_schema.TABLE_CONSTRAINTS AS pk
          ON pk.CONSTRAINT_SCHEMA = t.TABLE_SCHEMA
         AND pk.TABLE_NAME = t.TABLE_NAME
         AND pk.CONSTRAINT_TYPE = 'PRIMARY KEY'
        WHERE t.TABLE_SCHEMA = DATABASE()
          AND t.TABLE_TYPE = 'BASE TABLE'
          AND pk.CONSTRAINT_NAME IS NULL
        ORDER BY (COALESCE(t.DATA_LENGTH, 0) + COALESCE(t.INDEX_LENGTH, 0)) DESC,
                 t.TABLE_NAME
        """);

    Console.WriteLine("\n=== PARTITIONNEMENT ===");
    await PrintRows(
        connection,
        """
        SELECT TABLE_NAME AS table_nom,
               PARTITION_METHOD AS methode,
               PARTITION_EXPRESSION AS expression,
               COUNT(*) AS partitions,
               MIN(PARTITION_DESCRIPTION) AS premiere_borne,
               MAX(PARTITION_DESCRIPTION) AS derniere_borne
        FROM information_schema.PARTITIONS
        WHERE TABLE_SCHEMA = DATABASE()
          AND PARTITION_NAME IS NOT NULL
        GROUP BY TABLE_NAME, PARTITION_METHOD, PARTITION_EXPRESSION
        ORDER BY TABLE_NAME
        """);

    Console.WriteLine("\n=== EMPREINTE HISTORIQUE ET TECHNIQUE ===");
    await PrintRows(
        connection,
        """
        SELECT categorie,
               COUNT(*) AS tables,
               SUM(lignes_estimees) AS lignes_estimees,
               ROUND(SUM(octets) / 1024 / 1024 / 1024, 2) AS taille_gib
        FROM (
          SELECT CASE
                   WHEN TABLE_NAME REGEXP '(^_|backup|archive|_old|old_|temporary|temp_)'
                     THEN 'sauvegarde_temporaire_legacy'
                   WHEN TABLE_NAME REGEXP 'history|historique'
                     THEN 'historique'
                   WHEN TABLE_NAME REGEXP '(^|_)log($|_)|^log'
                     THEN 'journalisation'
                   WHEN TABLE_NAME REGEXP '(^|_)kpi($|_)|^reports|^view_'
                     THEN 'kpi_rapport_vue'
                   ELSE 'operationnel_autre'
                 END AS categorie,
                 COALESCE(TABLE_ROWS, 0) AS lignes_estimees,
                 COALESCE(DATA_LENGTH, 0) + COALESCE(INDEX_LENGTH, 0) AS octets
          FROM information_schema.TABLES
          WHERE TABLE_SCHEMA = DATABASE()
            AND TABLE_TYPE = 'BASE TABLE'
        ) AS classes
        GROUP BY categorie
        ORDER BY SUM(octets) DESC
        """);

    Console.WriteLine("\n=== ROUTINES ===");
    await PrintRows(
        connection,
        """
        SELECT ROUTINE_TYPE AS type_routine,
               ROUTINE_NAME AS routine,
               SQL_DATA_ACCESS AS acces_donnees,
               IS_DETERMINISTIC AS deterministe,
               SECURITY_TYPE AS securite
        FROM information_schema.ROUTINES
        WHERE ROUTINE_SCHEMA = DATABASE()
        ORDER BY ROUTINE_TYPE, ROUTINE_NAME
        """);

    Console.WriteLine("\n=== DEPENDANCES DES VUES ===");
    await PrintRows(
        connection,
        """
        SELECT VIEW_NAME AS vue,
               GROUP_CONCAT(DISTINCT TABLE_NAME ORDER BY TABLE_NAME SEPARATOR ', ') AS dependances
        FROM information_schema.VIEW_TABLE_USAGE
        WHERE VIEW_SCHEMA = DATABASE()
        GROUP BY VIEW_NAME
        ORDER BY VIEW_NAME
        """);
}

static async Task PrintSchemaFamilies(MySqlConnection connection)
{
    Console.WriteLine("=== PLUS GROS OBJETS PAR COUCHE FONCTIONNELLE ===");
    await PrintRows(
        connection,
        """
        WITH classified AS (
          SELECT CASE
                   WHEN TABLE_NAME REGEXP '^(parcel|shipment|shipping|livraison|signatures|photo)'
                     THEN 'transaction_colis'
                   WHEN TABLE_NAME REGEXP '^(billing|rsv|kpi|agent_invoice|credit)'
                     THEN 'facturation_kpi'
                   WHEN TABLE_NAME REGEXP '^(live|location|depot|route|sector|pickup|conveyor|sac|container)'
                     THEN 'routage_operation'
                   WHEN TABLE_NAME REGEXP '^(edi|ups|broker|shopify|fresh|jira|penguin|crm)'
                     THEN 'integration_externe'
                   WHEN TABLE_NAME REGEXP '^(customer|client)'
                     THEN 'client_configuration'
                   WHEN TABLE_NAME REGEXP '^(conversation|support|chatbot|ai_)'
                     THEN 'support_ia'
                   ELSE NULL
                 END AS couche,
                 TABLE_NAME,
                 TABLE_TYPE,
                 COALESCE(TABLE_ROWS, 0) AS lignes_estimees,
                 COALESCE(DATA_LENGTH, 0) + COALESCE(INDEX_LENGTH, 0) AS octets
          FROM information_schema.TABLES
          WHERE TABLE_SCHEMA = DATABASE()
        ), ranked AS (
          SELECT couche,
                 TABLE_NAME,
                 TABLE_TYPE,
                 lignes_estimees,
                 octets,
                 ROW_NUMBER() OVER (PARTITION BY couche ORDER BY octets DESC, TABLE_NAME) AS rang
          FROM classified
          WHERE couche IS NOT NULL
        )
        SELECT couche,
               TABLE_NAME AS table_nom,
               TABLE_TYPE AS type_objet,
               lignes_estimees,
               ROUND(octets / 1024 / 1024 / 1024, 2) AS taille_gib
        FROM ranked
        WHERE rang <= 12
        ORDER BY couche, rang
        """);

    Console.WriteLine("\n=== PLUS GROS OBJETS LEGACY, BACKUP ET TEMPORAIRES ===");
    await PrintRows(
        connection,
        """
        SELECT TABLE_NAME AS table_nom,
               TABLE_ROWS AS lignes_estimees,
               ROUND((COALESCE(DATA_LENGTH, 0) + COALESCE(INDEX_LENGTH, 0)) / 1024 / 1024 / 1024, 2) AS taille_gib,
               UPDATE_TIME AS derniere_modification_estimee
        FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_TYPE = 'BASE TABLE'
          AND TABLE_NAME REGEXP '(^_|backup|archive|_old|old_|temporary|temp_)'
        ORDER BY (COALESCE(DATA_LENGTH, 0) + COALESCE(INDEX_LENGTH, 0)) DESC,
                 TABLE_NAME
        LIMIT 30
        """);

    Console.WriteLine("\n=== STRUCTURE DES TABLES SANS CLE PRIMAIRE ===");
    await PrintRows(
        connection,
        """
        SELECT TABLE_NAME AS table_nom,
               ORDINAL_POSITION AS position,
               COLUMN_NAME AS colonne,
               COLUMN_TYPE AS type,
               IS_NULLABLE AS nullable,
               COLUMN_KEY AS cle
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME IN ('clientcompte', 'customer_sub_account')
        ORDER BY TABLE_NAME, ORDINAL_POSITION
        """);
}

static async Task VerifyKeyMetadata(MySqlConnection connection)
{
    Console.WriteLine("=== STATISTICS ===");
    await PrintRows(
        connection,
        """
        SELECT TABLE_NAME AS table_nom,
               INDEX_NAME AS index_nom,
               NON_UNIQUE AS non_unique,
               GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX SEPARATOR ', ') AS colonnes
        FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME IN ('clientcompte', 'customer_sub_account')
        GROUP BY TABLE_NAME, INDEX_NAME, NON_UNIQUE
        ORDER BY TABLE_NAME, INDEX_NAME
        """);

    Console.WriteLine("\n=== TABLE_CONSTRAINTS ===");
    await PrintRows(
        connection,
        """
        SELECT TABLE_NAME AS table_nom,
               CONSTRAINT_NAME AS contrainte,
               CONSTRAINT_TYPE AS type_contrainte
        FROM information_schema.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME IN ('clientcompte', 'customer_sub_account')
        ORDER BY TABLE_NAME, CONSTRAINT_NAME
        """);

    Console.WriteLine("\n=== DDL CLIENTCOMPTE ===");
    await PrintRows(connection, "SHOW CREATE TABLE clientcompte");

    Console.WriteLine("\n=== DDL CUSTOMER_SUB_ACCOUNT ===");
    await PrintRows(connection, "SHOW CREATE TABLE customer_sub_account");
}

static async Task ExplainIntegrityCheck(MySqlConnection connection)
{
    Console.WriteLine("=== PLAN : COUVERTURE DES PARENTS DE PARCEL ===");
    await PrintRows(
        connection,
        """
        EXPLAIN
        SELECT COUNT(*) AS total_colis,
               SUM(s.ID IS NULL) AS sans_shipment,
               SUM(c.CUSTOMER_ID IS NULL) AS sans_customer,
               SUM(sh.shipping_id IS NULL) AS sans_shipping
        FROM parcel AS p
        LEFT JOIN shipment AS s
          ON s.ID = p.SHIPMENT_INTERNAL_ID
         AND s.INSERT_DATE >= '2026-01-01'
         AND s.INSERT_DATE < '2027-01-01'
        LEFT JOIN customer AS c
          ON c.CUSTOMER_ID = p.CUSTOMER_ID
        LEFT JOIN shipping AS sh
          ON sh.shipping_id = p.SHIPPING_ID
        WHERE p.INSERT_DATE >= '2026-07-09'
          AND p.INSERT_DATE < '2026-07-10'
        """);

    Console.WriteLine("\n=== PLAN : COUVERTURE CUSTOMER DE SHIPMENT ===");
    await PrintRows(
        connection,
        """
        EXPLAIN
        SELECT COUNT(*) AS total_expeditions,
               SUM(c.CUSTOMER_ID IS NULL) AS sans_customer
        FROM shipment AS s
        LEFT JOIN customer AS c
          ON c.CUSTOMER_ID = s.CUSTOMER_ID
        WHERE s.INSERT_DATE >= '2026-07-09'
          AND s.INSERT_DATE < '2026-07-10'
        """);
}

static async Task RunIntegrityCheck(MySqlConnection connection)
{
    Console.WriteLine("=== INTEGRITE PARCEL, 2026-07-09 ===");
    await PrintRows(
        connection,
        """
        SELECT COUNT(*) AS total_colis,
               SUM(s.ID IS NULL) AS sans_shipment,
               ROUND(100 * SUM(s.ID IS NULL) / NULLIF(COUNT(*), 0), 4) AS taux_sans_shipment_pct,
               SUM(c.CUSTOMER_ID IS NULL) AS sans_customer,
               ROUND(100 * SUM(c.CUSTOMER_ID IS NULL) / NULLIF(COUNT(*), 0), 4) AS taux_sans_customer_pct,
               SUM(sh.shipping_id IS NULL) AS sans_shipping,
               ROUND(100 * SUM(sh.shipping_id IS NULL) / NULLIF(COUNT(*), 0), 4) AS taux_sans_shipping_pct
        FROM parcel AS p
        LEFT JOIN shipment AS s
          ON s.ID = p.SHIPMENT_INTERNAL_ID
         AND s.INSERT_DATE >= '2026-01-01'
         AND s.INSERT_DATE < '2027-01-01'
        LEFT JOIN customer AS c
          ON c.CUSTOMER_ID = p.CUSTOMER_ID
        LEFT JOIN shipping AS sh
          ON sh.shipping_id = p.SHIPPING_ID
        WHERE p.INSERT_DATE >= '2026-07-09'
          AND p.INSERT_DATE < '2026-07-10'
        """);

    Console.WriteLine("\n=== INTEGRITE SHIPMENT, 2026-07-09 ===");
    await PrintRows(
        connection,
        """
        SELECT COUNT(*) AS total_expeditions,
               SUM(c.CUSTOMER_ID IS NULL) AS sans_customer,
               ROUND(100 * SUM(c.CUSTOMER_ID IS NULL) / NULLIF(COUNT(*), 0), 4) AS taux_sans_customer_pct
        FROM shipment AS s
        LEFT JOIN customer AS c
          ON c.CUSTOMER_ID = s.CUSTOMER_ID
        WHERE s.INSERT_DATE >= '2026-07-09'
          AND s.INSERT_DATE < '2026-07-10'
        """);
}

static async Task InvestigateShipmentKey(MySqlConnection connection, bool explainOnly)
{
    if (!explainOnly)
    {
        Console.WriteLine("=== SHIPMENT_NEW : METADONNEES ===");
        await PrintRows(
            connection,
            """
            SELECT t.TABLE_NAME AS table_nom,
                   t.TABLE_ROWS AS lignes_estimees,
                   ROUND((COALESCE(t.DATA_LENGTH, 0) + COALESCE(t.INDEX_LENGTH, 0)) / 1024 / 1024, 2) AS taille_mib,
                   COUNT(c.COLUMN_NAME) AS colonnes
            FROM information_schema.TABLES AS t
            JOIN information_schema.COLUMNS AS c
              ON c.TABLE_SCHEMA = t.TABLE_SCHEMA
             AND c.TABLE_NAME = t.TABLE_NAME
            WHERE t.TABLE_SCHEMA = DATABASE()
              AND t.TABLE_NAME = 'shipment_new'
            GROUP BY t.TABLE_NAME, t.TABLE_ROWS, t.DATA_LENGTH, t.INDEX_LENGTH
            """);

        Console.WriteLine("\n=== DISTRIBUTION DES IDENTIFIANTS PARCEL ===");
        await PrintRows(
            connection,
            """
            SELECT COUNT(*) AS total_colis,
                   COUNT(DISTINCT SHIPMENT_INTERNAL_ID) AS shipment_internal_distincts,
                   MIN(SHIPMENT_INTERNAL_ID) AS shipment_internal_min,
                   MAX(SHIPMENT_INTERNAL_ID) AS shipment_internal_max,
                   SUM(SHIPMENT_INTERNAL_ID = 0) AS shipment_internal_zero,
                   COUNT(DISTINCT SHIPPING_ID) AS shipping_distincts,
                   MIN(SHIPPING_ID) AS shipping_min,
                   MAX(SHIPPING_ID) AS shipping_max
            FROM parcel
            WHERE INSERT_DATE >= '2026-07-09'
              AND INSERT_DATE < '2026-07-10'
            """);
    }

    Console.WriteLine(explainOnly
        ? "=== PLAN : CLES CANDIDATES SUR 5000 COLIS ==="
        : "\n=== COUVERTURE DES CLES CANDIDATES SUR 5000 COLIS ===");

    var query =
        """
        WITH recent_parcel AS (
          SELECT ID,
                 INSERT_DATE,
                 SHIPMENT_INTERNAL_ID,
                 SHIPPING_ID,
                 EXP_DATE
          FROM parcel
          WHERE INSERT_DATE >= '2026-07-09'
            AND INSERT_DATE < '2026-07-10'
          ORDER BY INSERT_DATE, ID
          LIMIT 5000
        )
        SELECT COUNT(*) AS colis_echantillon,
               SUM(EXISTS(
                 SELECT 1 FROM shipment AS s
                 WHERE s.ID = p.SHIPMENT_INTERNAL_ID
               )) AS correspondance_shipment_id,
               SUM(EXISTS(
                 SELECT 1 FROM shipment_new AS sn
                 WHERE sn.ID = p.SHIPMENT_INTERNAL_ID
               )) AS correspondance_shipment_new_id,
               SUM(EXISTS(
                 SELECT 1 FROM shipment AS s
                 WHERE s.SHIPPING_ID = p.SHIPPING_ID
                   AND s.EXP_DATE = p.EXP_DATE
               )) AS correspondance_shipment_shipping_expdate,
               SUM(EXISTS(
                 SELECT 1 FROM shipment_new AS sn
                 WHERE sn.SHIPPING_ID = p.SHIPPING_ID
                   AND sn.EXP_DATE = p.EXP_DATE
               )) AS correspondance_shipment_new_shipping_expdate,
               SUM(EXISTS(
                 SELECT 1 FROM shipping AS sh
                 WHERE sh.shipping_id = p.SHIPPING_ID
               )) AS correspondance_shipping_id
        FROM recent_parcel AS p
        """;

    await PrintRows(connection, explainOnly ? $"EXPLAIN {query}" : query);
}

static async Task RunCorrectedIntegrityCheck(MySqlConnection connection, bool explainOnly)
{
    var parcelQuery =
        """
        SELECT COUNT(*) AS total_colis,
               SUM(NOT EXISTS(
                 SELECT 1 FROM shipment AS s
                 WHERE s.SHIPPING_ID = p.SHIPPING_ID
                   AND s.EXP_DATE = p.EXP_DATE
               )) AS sans_shipment_metier,
               ROUND(100 * SUM(NOT EXISTS(
                 SELECT 1 FROM shipment AS s
                 WHERE s.SHIPPING_ID = p.SHIPPING_ID
                   AND s.EXP_DATE = p.EXP_DATE
               )) / NULLIF(COUNT(*), 0), 4) AS taux_sans_shipment_metier_pct,
               SUM(NOT EXISTS(
                 SELECT 1 FROM customer AS c
                 WHERE c.CUSTOMER_ID = p.CUSTOMER_ID
               )) AS sans_customer,
               SUM(NOT EXISTS(
                 SELECT 1 FROM shipping AS sh
                 WHERE sh.shipping_id = p.SHIPPING_ID
               )) AS sans_shipping_legacy
        FROM parcel AS p
        WHERE p.INSERT_DATE >= '2026-07-09'
          AND p.INSERT_DATE < '2026-07-10'
        """;

    Console.WriteLine(explainOnly
        ? "=== PLAN : INTEGRITE PARCEL CORRIGEE ==="
        : "=== INTEGRITE PARCEL CORRIGEE, 2026-07-09 ===");
    await PrintRows(connection, explainOnly ? $"EXPLAIN {parcelQuery}" : parcelQuery);

    var historyQuery =
        """
        WITH recent_history AS (
          SELECT PARCEL_HISTORY_ID,
                 DATE_INSERT,
                 PARCEL_ID,
                 SHIPPING_ID,
                 CUSTOMER_ID
          FROM parcel_history
          WHERE DATE_INSERT >= '2026-07-09'
            AND DATE_INSERT < '2026-07-10'
          ORDER BY DATE_INSERT, PARCEL_HISTORY_ID
          LIMIT 5000
        )
        SELECT COUNT(*) AS evenements_echantillon,
               SUM(EXISTS(
                 SELECT 1 FROM parcel AS p
                 WHERE p.PARCEL_ID = h.PARCEL_ID
                   AND p.SHIPPING_ID = h.SHIPPING_ID
               )) AS correspondance_parcel_shipping,
               SUM(EXISTS(
                 SELECT 1 FROM shipment AS s
                 WHERE s.SHIPPING_ID = h.SHIPPING_ID
               )) AS correspondance_shipment_shipping,
               SUM(EXISTS(
                 SELECT 1 FROM customer AS c
                 WHERE c.CUSTOMER_ID = h.CUSTOMER_ID
               )) AS correspondance_customer
        FROM recent_history AS h
        """;

    Console.WriteLine(explainOnly
        ? "\n=== PLAN : CLES PARCEL_HISTORY SUR 5000 EVENEMENTS ==="
        : "\n=== CLES PARCEL_HISTORY SUR 5000 EVENEMENTS ===");
    await PrintRows(connection, explainOnly ? $"EXPLAIN {historyQuery}" : historyQuery);
}

static async Task SegmentHistoryIntegrity(MySqlConnection connection)
{
    Console.WriteLine("=== INTEGRITE PARCEL_HISTORY PAR SOURCE, ECHANTILLON 5000 ===");
    await PrintRows(
        connection,
        """
        WITH recent_history AS (
          SELECT PARCEL_HISTORY_ID,
                 DATE_INSERT,
                 PARCEL_ID,
                 SHIPPING_ID,
                 CUSTOMER_ID,
                 SOURCE_TYPE,
                 SHIPPING_TYPE
          FROM parcel_history
          WHERE DATE_INSERT >= '2026-07-09'
            AND DATE_INSERT < '2026-07-10'
          ORDER BY DATE_INSERT, PARCEL_HISTORY_ID
          LIMIT 5000
        )
        SELECT SOURCE_TYPE AS source_type,
               SHIPPING_TYPE AS shipping_type,
               COUNT(*) AS evenements,
               SUM(PARCEL_ID IS NULL OR PARCEL_ID = 0) AS parcel_id_vide,
               SUM(SHIPPING_ID IS NULL OR SHIPPING_ID = 0) AS shipping_id_vide,
               SUM(CUSTOMER_ID IS NULL OR CUSTOMER_ID = 0) AS customer_id_vide,
               SUM(NOT EXISTS(
                 SELECT 1 FROM parcel AS p
                 WHERE p.PARCEL_ID = h.PARCEL_ID
                   AND p.SHIPPING_ID = h.SHIPPING_ID
               )) AS sans_parcel,
               SUM(NOT EXISTS(
                 SELECT 1 FROM shipment AS s
                 WHERE s.SHIPPING_ID = h.SHIPPING_ID
               )) AS sans_shipment,
               SUM(NOT EXISTS(
                 SELECT 1 FROM customer AS c
                 WHERE c.CUSTOMER_ID = h.CUSTOMER_ID
               )) AS sans_customer
        FROM recent_history AS h
        GROUP BY SOURCE_TYPE, SHIPPING_TYPE
        ORDER BY (sans_parcel + sans_shipment + sans_customer) DESC,
                 evenements DESC,
                 SOURCE_TYPE,
                 SHIPPING_TYPE
        """);
}

static async Task InspectSourceTypeMetadata(MySqlConnection connection)
{
    Console.WriteLine("=== COLONNES SOURCE_TYPE ===");
    await PrintRows(
        connection,
        """
        SELECT TABLE_NAME AS table_nom,
               COLUMN_NAME AS colonne,
               COLUMN_TYPE AS type,
               COLUMN_COMMENT AS commentaire
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND COLUMN_NAME IN ('SOURCE_TYPE', 'SOURCE_TYPE_ID', 'TYPE_SOURCE')
        ORDER BY TABLE_NAME, COLUMN_NAME
        """);

    Console.WriteLine("\n=== TABLES CANDIDATES DE REFERENCE ===");
    await PrintRows(
        connection,
        """
        SELECT TABLE_NAME AS table_nom,
               TABLE_ROWS AS lignes_estimees,
               TABLE_COMMENT AS commentaire
        FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE()
          AND (
            TABLE_NAME REGEXP 'source.*type|type.*source'
            OR TABLE_NAME REGEXP 'parcel.*source|history.*source'
          )
        ORDER BY TABLE_NAME
        """);
}

static async Task InspectNatClikIntegrity(MySqlConnection connection, bool explainOnly)
{
    Console.WriteLine(explainOnly
        ? "=== PLAN : CHEMINS CANDIDATS NAT_CLIK API ==="
        : "=== CHEMINS CANDIDATS NAT_CLIK API, ECHANTILLON INITIAL 5000 ===");

    var query =
        """
        WITH recent_history AS (
          SELECT PARCEL_HISTORY_ID,
                 DATE_INSERT,
                 PARCEL_ID,
                 SHIPPING_ID,
                 CUSTOMER_ID,
                 SOURCE_TYPE
          FROM parcel_history
          WHERE DATE_INSERT >= '2026-07-09'
            AND DATE_INSERT < '2026-07-10'
          ORDER BY DATE_INSERT, PARCEL_HISTORY_ID
          LIMIT 5000
        )
        SELECT COUNT(*) AS evenements_natclik,
               SUM(EXISTS(
                 SELECT 1 FROM parcel AS p
                 WHERE p.PARCEL_ID = h.PARCEL_ID
                   AND p.SHIPPING_ID = h.SHIPPING_ID
               )) AS dans_parcel_courant,
               SUM(EXISTS(
                 SELECT 1 FROM _parcel_old AS po
                 WHERE po.PARCEL_ID = h.PARCEL_ID
                   AND po.SHIPPING_ID = h.SHIPPING_ID
               )) AS dans_parcel_old,
               SUM(EXISTS(
                 SELECT 1 FROM shipment AS s
                 WHERE s.SHIPPING_ID = h.SHIPPING_ID
               )) AS dans_shipment_courant,
               SUM(EXISTS(
                 SELECT 1 FROM shipping AS sh
                 WHERE sh.shipping_id = h.SHIPPING_ID
               )) AS dans_shipping_legacy,
               SUM(EXISTS(
                 SELECT 1 FROM livraison AS l
                 WHERE l.BILLNUMEXP = h.SHIPPING_ID
               )) AS dans_livraison_legacy
        FROM recent_history AS h
        WHERE h.SOURCE_TYPE = 600
        """;

    await PrintRows(connection, explainOnly ? $"EXPLAIN {query}" : query);
}

static async Task InspectSourceTypeCoverage(MySqlConnection connection, bool explainOnly)
{
    Console.WriteLine(explainOnly
        ? "=== PLAN : COUVERTURE DU REFERENTIEL SOURCE_TYPE ==="
        : "=== COUVERTURE DU REFERENTIEL SOURCE_TYPE, 2026-07-09 ===");

    var query =
        """
        SELECT h.SOURCE_TYPE AS source_type,
               COALESCE(r.SOURCE_TYPE_FR, '(non documente)') AS libelle,
               COUNT(*) AS evenements,
               ROUND(100 * COUNT(*) / SUM(COUNT(*)) OVER (), 4) AS part_pct
        FROM parcel_history AS h
        LEFT JOIN parcel_history_source_type AS r
          ON r.SOURCE_TYPE = h.SOURCE_TYPE
        WHERE h.DATE_INSERT >= '2026-07-09'
          AND h.DATE_INSERT < '2026-07-10'
        GROUP BY h.SOURCE_TYPE, r.SOURCE_TYPE_FR
        ORDER BY evenements DESC, h.SOURCE_TYPE
        """;

    await PrintRows(connection, explainOnly ? $"EXPLAIN {query}" : query);
}

static async Task InspectNatClikFullDay(MySqlConnection connection, bool explainOnly)
{
    Console.WriteLine(explainOnly
        ? "=== PLAN : NAT_CLIK JOURNEE COMPLETE ==="
        : "=== NAT_CLIK JOURNEE COMPLETE, 2026-07-09 ===");

    var query =
        """
        SELECT COUNT(*) AS evenements_natclik,
               SUM(EXISTS(
                 SELECT 1 FROM parcel AS p
                 WHERE p.PARCEL_ID = h.PARCEL_ID
                   AND p.SHIPPING_ID = h.SHIPPING_ID
               )) AS dans_parcel_courant,
               SUM(EXISTS(
                 SELECT 1 FROM shipment AS s
                 WHERE s.SHIPPING_ID = h.SHIPPING_ID
               )) AS dans_shipment_courant,
               SUM(EXISTS(
                 SELECT 1 FROM shipping AS sh
                 WHERE sh.shipping_id = h.SHIPPING_ID
               )) AS dans_shipping_legacy,
               SUM(EXISTS(
                 SELECT 1 FROM livraison AS l
                 WHERE l.BILLNUMEXP = h.SHIPPING_ID
               )) AS dans_livraison_legacy,
               SUM(NOT EXISTS(
                 SELECT 1 FROM customer AS c
                 WHERE c.CUSTOMER_ID = h.CUSTOMER_ID
               )) AS sans_customer
        FROM parcel_history AS h
        WHERE h.DATE_INSERT >= '2026-07-09'
          AND h.DATE_INSERT < '2026-07-10'
          AND h.SOURCE_TYPE = 600
        """;

    await PrintRows(connection, explainOnly ? $"EXPLAIN {query}" : query);
}

static async Task PrintRows(MySqlConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await using var reader = await command.ExecuteReaderAsync();

    var headers = Enumerable.Range(0, reader.FieldCount)
        .Select(reader.GetName);
    Console.WriteLine(string.Join("\t", headers));

    while (await reader.ReadAsync())
    {
        var values = Enumerable.Range(0, reader.FieldCount)
            .Select(index => reader.IsDBNull(index) ? "NULL" : Convert.ToString(reader.GetValue(index)) ?? string.Empty);
        Console.WriteLine(string.Join("\t", values));
    }
}
