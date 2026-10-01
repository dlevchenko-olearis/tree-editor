using Microsoft.EntityFrameworkCore;

namespace TreeEditor.Infrastructure.Persistence;

/// <summary>EF Core model of the tree database. Used for migrations and as the connection for raw SQL.</summary>
internal sealed class TreeDbContext(DbContextOptions<TreeDbContext> options) : DbContext(options)
{
    /// <summary>Name of the sequence that provides node versions.</summary>
    public const string VersionSequence = "node_version_seq";

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(VersionSequence);

        modelBuilder.Entity<NodeRecord>(node =>
        {
            node.ToTable("nodes", table => table.HasCheckConstraint("ck_nodes_parent_is_not_self", "parent_id <> id"));
            node.HasKey(n => n.Id).HasName("pk_nodes");
            node.Property(n => n.Id).HasColumnName("id").ValueGeneratedNever();
            node.Property(n => n.ParentId).HasColumnName("parent_id");
            node.Property(n => n.Value).HasColumnName("value").HasMaxLength(200).IsRequired();
            node.Property(n => n.IsDeleted).HasColumnName("is_deleted").HasDefaultValueSql("false");
            node.Property(n => n.Version).HasColumnName("version").HasDefaultValueSql($"nextval('{VersionSequence}')");
            node.HasOne<NodeRecord>()
                .WithMany()
                .HasForeignKey(n => n.ParentId)
                .HasConstraintName("fk_nodes_parent")
                .OnDelete(DeleteBehavior.Restrict);
            node.HasIndex(n => new { n.ParentId, n.Id }).HasDatabaseName("ix_nodes_parent_id_id");
            node.HasIndex(n => n.Id).HasDatabaseName("ix_nodes_roots").HasFilter("parent_id IS NULL");
        });

        modelBuilder.Entity<TreeStateRecord>(state =>
        {
            state.ToTable("tree_state", table => table.HasCheckConstraint("ck_tree_state_single_row", "id = 1"));
            state.HasKey(s => s.Id).HasName("pk_tree_state");
            state.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            state.Property(s => s.Generation).HasColumnName("generation");
            state.HasData(new TreeStateRecord { Id = 1, Generation = 1 });
        });

        modelBuilder.Entity<ApplyRequestRecord>(request =>
        {
            request.ToTable("apply_requests");
            request.HasKey(r => r.Key).HasName("pk_apply_requests");
            request.Property(r => r.Key).HasColumnName("key").ValueGeneratedNever();
            request.Property(r => r.RequestHash).HasColumnName("request_hash").IsRequired();
            request.Property(r => r.Response).HasColumnName("response").HasColumnType("jsonb");
            request.Property(r => r.CreatedAt).HasColumnName("created_at");
            request.HasIndex(r => r.CreatedAt).HasDatabaseName("ix_apply_requests_created_at");
        });
    }
}
