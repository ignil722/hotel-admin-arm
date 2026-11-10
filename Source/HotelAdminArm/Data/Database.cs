using System.Data;
using HotelAdminArm;
using Microsoft.Data.Sqlite;

namespace HotelAdminArm.Data;

internal static partial class Database
{
    private const string DateFormat = "yyyy-MM-dd";

    public static string DatabasePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HotelAdminArm", "hotel.db");
    public static string DataFolder => Path.GetDirectoryName(Path.GetFullPath(DatabasePath))!;
    public static Func<DateTime> Clock { get; set; } = () => DateTime.Now;
    public static DateTime Now => Clock();
    public static DateTime Today => Clock().Date;

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

    public static void Initialize(bool demonstrationData = true)
    {
        var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(DatabasePath).ToUpperInvariant())));
        using var startup=new Mutex(false,"Local\\HotelAdminArm-"+key);
        var acquired=false;
        try
        {
            try { acquired=startup.WaitOne(TimeSpan.FromSeconds(30)); }
            catch(AbandonedMutexException) { acquired=true; }
            if(!acquired)throw new InvalidOperationException("Другой экземпляр обновляет базу. Повторите запуск после завершения обновления.");
            Directory.CreateDirectory(DataFolder);
            using var connection=OpenConnection();
            bool fresh;
            using(var tx=connection.BeginTransaction())
            {
                fresh=Scalar<long>(connection,tx,"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Rooms'")==0;
                if(fresh)
                {
                    Execute(connection,tx,SchemaSql);
                    if(demonstrationData)SeedValues(connection,tx);
                }
                tx.Commit();
            }
            UpgradeSchema(connection,fresh);
        }
        finally { if(acquired)startup.ReleaseMutex(); }
    }

    public static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        // Функции регистрируются на каждом подключении: триггеры проверяют те же местные дату и время.
        connection.CreateFunction("hotel_meal_deadline", (string kind) => kind=="None"?"24:00:00":Domain.HotelService.MealDeadline(kind).ToString(@"hh\:mm\:ss"), isDeterministic: true);
        connection.CreateFunction("hotel_now", () => Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), isDeterministic: false);
        connection.CreateFunction("hotel_today", () => Today.ToString(DateFormat), isDeterministic: false);
        connection.CreateFunction("hotel_passport", (string value) => Domain.HotelService.NormalizePassport(value), isDeterministic: true);
        connection.CreateFunction("hotel_meal_kind", (string value) => Domain.HotelService.SuggestedMealKind(value), isDeterministic: true);

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
        return Read(connection, null, sql, parameters);
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
            DateTime dateTime => dateTime.ToString(DateFormat, System.Globalization.CultureInfo.InvariantCulture),
            decimal decimalValue => Convert.ToDouble(decimalValue),
            _ => value
        };
    }

}
