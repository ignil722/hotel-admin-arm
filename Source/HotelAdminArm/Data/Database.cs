using System.Data;
using HotelAdminArm;
using Microsoft.Data.Sqlite;

namespace HotelAdminArm.Data;

internal static partial class Database
{
    private const string DateFormat = "yyyy-MM-dd";

    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HotelAdminArm");

    public static string DatabasePath { get; } = Path.Combine(DataFolder, "hotel.db");

    public static string ConnectionString
    {
        get
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                ForeignKeys = true
            };

            return builder.ToString();
        }
    }

    public static void Initialize()
    {
        Directory.CreateDirectory(DataFolder);

        using var connection = OpenConnection();
        ExecuteBatch(connection, SchemaSql);

        var roomCount = Scalar<long>(connection, null, "SELECT COUNT(*) FROM Rooms");
        if (roomCount == 0)
        {
            Seed(connection);
        }

        ApplyMigrations(connection);
    }

    public static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();

        return connection;
    }

    public static SqliteParameter P(string name, object? value)
    {
        if (!name.StartsWith('@'))
        {
            name = "@" + name;
        }

        return new SqliteParameter(name, NormalizeValue(value));
    }

    public static DataTable Query(string sql, params SqliteParameter[] parameters)
    {
        using var connection = OpenConnection();
        using var command = BuildCommand(connection, null, sql, parameters);
        using var reader = command.ExecuteReader();

        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    public static int Execute(string sql, params SqliteParameter[] parameters)
    {
        using var connection = OpenConnection();
        using var command = BuildCommand(connection, null, sql, parameters);
        return command.ExecuteNonQuery();
    }

    public static T Scalar<T>(string sql, params SqliteParameter[] parameters)
    {
        using var connection = OpenConnection();
        return Scalar<T>(connection, null, sql, parameters);
    }

    public static void ExecuteInTransaction(Action<SqliteConnection, SqliteTransaction> action)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            action(connection, transaction);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public static int Execute(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params SqliteParameter[] parameters)
    {
        using var command = BuildCommand(connection, transaction, sql, parameters);
        return command.ExecuteNonQuery();
    }

    public static T Scalar<T>(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params SqliteParameter[] parameters)
    {
        using var command = BuildCommand(connection, transaction, sql, parameters);
        var value = command.ExecuteScalar();

        if (value is null || value == DBNull.Value)
        {
            return default!;
        }

        return (T)Convert.ChangeType(value, typeof(T));
    }

    public static long Insert(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params SqliteParameter[] parameters)
    {
        Execute(connection, transaction, sql, parameters);
        return Scalar<long>(connection, transaction, "SELECT last_insert_rowid();");
    }

    private static SqliteCommand BuildCommand(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params SqliteParameter[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;

        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static object NormalizeValue(object? value)
    {
        return value switch
        {
            null => DBNull.Value,
            DateTime dateTime => dateTime.ToString(DateFormat),
            decimal decimalValue => Convert.ToDouble(decimalValue),
            _ => value
        };
    }

    private static void ExecuteBatch(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void ApplyMigrations(SqliteConnection connection)
    {
        var designVersion = GetSetting(connection, "DesignVersion");
        if (designVersion == HotelBranding.DesignVersion.ToString())
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            Execute(
                connection,
                transaction,
                "INSERT OR REPLACE INTO AppSettings (Key, Value) VALUES ('HotelName', @hotelName);",
                P("hotelName", HotelBranding.HotelName));

            using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = "SELECT Id, Number, RoomTypeId FROM Rooms ORDER BY Id;";
            using var reader = select.ExecuteReader();

            var rooms = new List<(int Id, string Number, int RoomTypeId)>();
            while (reader.Read())
            {
                rooms.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
            }

            foreach (var room in rooms)
            {
                Execute(
                    connection,
                    transaction,
                    "UPDATE Rooms SET Photo = @photo, Notes = @notes WHERE Id = @id;",
                    P("photo", HotelBranding.CreateRoomImage(room.Number, room.RoomTypeId)),
                    P("notes", $"Номер гостиницы {HotelBranding.HotelName}. Фото обновлено в рамках дизайн-версии {HotelBranding.DesignVersion}."),
                    P("id", room.Id));
            }

            Execute(
                connection,
                transaction,
                "INSERT OR REPLACE INTO AppSettings (Key, Value) VALUES ('DesignVersion', @version);",
                P("version", HotelBranding.DesignVersion));

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static string? GetSetting(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppSettings WHERE Key = @key;";
        command.Parameters.Add(P("key", key));
        return Convert.ToString(command.ExecuteScalar());
    }
}
