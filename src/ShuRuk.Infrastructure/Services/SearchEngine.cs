using Microsoft.Data.Sqlite;

namespace ShuRuk.Infrastructure.Services;

public class SearchEngine
{
    private readonly string _connectionString;
    private bool _initialized;

    /// <summary>
    /// Creates a search engine that uses the specified SQLite database.
    /// </summary>
    /// <param name="dbPath">The path to the SQLite database file.</param>
    public SearchEngine(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>
    /// Ensures that the full-text search index is available for use.
    /// </summary>
    /// <param name="cancellationToken">A token that can be used to cancel the initialization operation.</param>
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

    /// <summary>
    /// Adds or updates a document in the full-text search index.
    /// </summary>
    /// <param name="documentId">The unique identifier of the document.</param>
    /// <param name="documentType">The type of the document.</param>
    /// <param name="title">The document title.</param>
    /// <param name="content">The searchable document content.</param>
    /// <param name="metadata">Optional metadata associated with the document.</param>
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

    /// <summary>
    /// Removes a document from the search index.
    /// </summary>
    /// <param name="documentId">The identifier of the document to remove.</param>
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

    /// <summary>
    /// Searches indexed documents and returns ranked matching results, optionally filtered by document type.
    /// </summary>
    /// <param name="query">The full-text search query.</param>
    /// <param name="documentType">The document type used to filter results, or <c>null</c> to search all document types.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <returns>The ranked search results matching the query.</returns>
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