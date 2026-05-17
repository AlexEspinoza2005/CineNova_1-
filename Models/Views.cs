using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MovieApi.Models.Views
{
    public class VDashboardSummary
    {
        [Column("total_movies")]
        public long? TotalMovies { get; set; }
        [Column("total_users")]
        public long? TotalUsers { get; set; }
        [Column("total_clients")]
        public long? TotalClients { get; set; }
        [Column("total_errors")]
        public long? TotalErrors { get; set; }
        [Column("total_agent_queries")]
        public long? TotalAgentQueries { get; set; }
        [Column("success_rate_pct")]
        public decimal? SuccessRatePct { get; set; }
        [Column("avg_insertion_latency_ms")]
        public decimal? AvgInsertionLatencyMs { get; set; }
        [Column("avg_query_latency_ms")]
        public decimal? AvgQueryLatencyMs { get; set; }
        [Column("avg_embedding_latency_ms")]
        public decimal? AvgEmbeddingLatencyMs { get; set; }
        [Column("total_rejected")]
        public long? TotalRejected { get; set; }
        [Column("total_duplicates")]
        public long? TotalDuplicates { get; set; }
        [Column("total_vectors_stored")]
        public long? TotalVectorsStored { get; set; }
    }

    public class VMoviesPerUser
    {
        [Column("user_id")]
        public Guid UserId { get; set; }
        [Column("email")]
        public string Email { get; set; } = string.Empty;
        [Column("username")]
        public string? Username { get; set; }
        [Column("full_name")]
        public string? FullName { get; set; }
        [Column("movie_count")]
        public long MovieCount { get; set; }
        [Column("error_count")]
        public long ErrorCount { get; set; }
        [Column("duplicate_count")]
        public long DuplicateCount { get; set; }
    }

    public class VRecentMovie
    {
        [Column("id")]
        public Guid Id { get; set; }
        [Column("title")]
        public string Title { get; set; } = string.Empty;
        [Column("genre")]
        public string Genre { get; set; } = string.Empty;
        [Column("director")]
        public string Director { get; set; } = string.Empty;
        [Column("year")]
        public int Year { get; set; }
        [Column("insertion_status")]
        public string InsertionStatus { get; set; } = string.Empty;
        [Column("insertion_latency_ms")]
        public int? InsertionLatencyMs { get; set; }
        [Column("embedding_latency_ms")]
        public int? EmbeddingLatencyMs { get; set; }
        [Column("ingestion_date")]
        public DateOnly IngestionDate { get; set; }
        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
        [Column("user_email")]
        public string UserEmail { get; set; } = string.Empty;
        [Column("username")]
        public string? Username { get; set; }
    }

    public class VErrorsByAction
    {
        [Column("action")]
        public string Action { get; set; } = string.Empty;
        [Column("total")]
        public long Total { get; set; }
        [Column("errors")]
        public long Errors { get; set; }
        [Column("successes")]
        public long Successes { get; set; }
        [Column("avg_latency_ms")]
        public decimal AvgLatencyMs { get; set; }
    }
}
