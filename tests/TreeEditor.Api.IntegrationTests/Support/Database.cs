using Npgsql;

namespace TreeEditor.Api.IntegrationTests.Support;

/// <summary>Direct SQL access to a test database, bypassing the application.</summary>
public sealed class Database(string connectionString)
{
    /// <summary>The connection string of the database.</summary>
    public string ConnectionString { get; } = connectionString;

    /// <summary>Runs a statement and returns the affected row count.</summary>
    public async Task<int> ExecuteAsync(string sql, params object[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>Runs a query and returns the first column of the first row.</summary>
    public async Task<T> ScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = Command(connection, sql, parameters);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Runs a query and maps every row.</summary>
    public async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> map, params object[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    /// <summary>Opens a new connection.</summary>
    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, string sql, object[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        return command;
    }
}
