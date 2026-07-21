using Microsoft.Data.Sqlite;

namespace ShuRuk.Infrastructure.Services;

public class SearchEngine
{
    private readonly string _connectionString;
    private bool _initialized;

    public SearchEngine(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE VIRTUAL TABLE IF NOT EXISTS search_index USING fts5(
                document_id,
                document_type,
                title,
                content,
                metadata,
                tokenize='unicode61'
            )";
        await command.ExecuteNonQueryAsync(cancellationToken);

        _initialized = true;
    }

    public async Task IndexAsync(string documentId, string documentType, string title, string content, string? metadata = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            DELETE FROM search_index WHERE document_id = @docId;
            INSERT INTO search_index (document_id, document_type, title, content, metadata)
            VALUES (@docId, @docType, @title, @content, @metadata)";
        command.Parameters.AddWithValue("@docId", documentId);
        command.Parameters.AddWithValue("@docType", documentType);
        command.Parameters.AddWithValue("@title", title);
        command.Parameters.AddWithValue("@content", content);
        command.Parameters.AddWithValue("@metadata", metadata ?? string.Empty);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveFromIndexAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM search_index WHERE document_id = @docId";
        command.Parameters.AddWithValue("@docId", documentId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, string? documentType = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var whereClause = documentType is not null
            ? "AND document_type = @docType"
            : string.Empty;

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT document_id, document_type, title, snippet(search_index, 3, '«', '»', '...', 16) as snippet, rank
            FROM search_index
            WHERE search_index MATCH @query {whereClause}
            ORDER BY rank
            LIMIT @limit";
        command.Parameters.AddWithValue("@query", query);
        command.Parameters.AddWithValue("@limit", limit);
        if (documentType is not null)
        {
            command.Parameters.AddWithValue("@docType", documentType);
        }

        var results = new List<SearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SearchResult
            {
                DocumentId = reader.GetString(0),
                DocumentType = reader.GetString(1),
                Title = reader.GetString(2),
                Snippet = reader.GetString(3),
                Rank = reader.GetDouble(4)
            });
        }

        return results;
    }
}

public class SearchResult
{
    public string DocumentId { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public double Rank { get; set; }
}