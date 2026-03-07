using DevAtlas.Configuration;
using DevAtlas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DevAtlas.Data
{
    public class DevAtlasDbContext : DbContext
    {
        private readonly ScalabilityConfiguration _config;
        private readonly ILogger<DevAtlasDbContext>? _logger;

        public DevAtlasDbContext(DbContextOptions<DevAtlasDbContext> options,
            ScalabilityConfiguration config,
            ILogger<DevAtlasDbContext>? logger = null) : base(options)
        {
            _config = config;
            _logger = logger;
        }

        public DbSet<Project> Projects { get; set; }
        public DbSet<ProjectTag> Tags { get; set; }
        public DbSet<ScanHistory> ScanHistory { get; set; }
        public DbSet<ProjectMetadata> ProjectMetadata { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var appFolder = Path.Combine(appDataPath, "DevAtlas");

                if (!Directory.Exists(appFolder))
                {
                    Directory.CreateDirectory(appFolder);
                }

                var dbPath = Path.Combine(appFolder, "devatlas.db");
                optionsBuilder.UseSqlite($"Data Source={dbPath}");

                if (_config.EnablePerformanceMonitoring)
                {
                    optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information);
                }
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Project entity
            modelBuilder.Entity<Project>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Path).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.ProjectType).HasMaxLength(100);
                entity.Property(e => e.Category).HasMaxLength(50);
                entity.Property(e => e.GitBranch).HasMaxLength(100);
                entity.Property(e => e.IconText).HasMaxLength(10);
                entity.Property(e => e.IconColor).HasMaxLength(20);

                if (_config.EnableDatabaseIndexing)
                {
                    entity.HasIndex(e => e.Path).IsUnique();
                    entity.HasIndex(e => e.Name);
                    entity.HasIndex(e => e.ProjectType);
                    entity.HasIndex(e => e.Category);
                    entity.HasIndex(e => e.LastModified);
                    entity.HasIndex(e => e.IsActive);
                }
            });

            // Configure ProjectTag entity
            modelBuilder.Entity<ProjectTag>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Color).HasMaxLength(20);
                entity.Property(e => e.Description).HasMaxLength(500);

                if (_config.EnableDatabaseIndexing)
                {
                    entity.HasIndex(e => e.Name).IsUnique();
                }
            });

            // Configure many-to-many relationship between Projects and Tags
            modelBuilder.Entity<Project>()
                .HasMany(p => p.Tags)
                .WithMany(t => t.Projects)
                .UsingEntity(j => j.ToTable("ProjectTags"));

            // Configure ScanHistory entity
            modelBuilder.Entity<ScanHistory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ScanType).HasMaxLength(50);
                entity.Property(e => e.Status).HasMaxLength(20);

                if (_config.EnableDatabaseIndexing)
                {
                    entity.HasIndex(e => e.ScanStartTime);
                    entity.HasIndex(e => e.Status);
                }
            });

            // Configure ProjectMetadata entity
            modelBuilder.Entity<ProjectMetadata>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ProjectPath).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.MetadataType).IsRequired().HasMaxLength(50);

                if (_config.EnableDatabaseIndexing)
                {
                    entity.HasIndex(e => e.ProjectPath);
                    entity.HasIndex(e => e.MetadataType);
                    entity.HasIndex(e => e.LastUpdated);
                }
            });
        }

        public async Task<bool> IsDatabaseHealthyAsync()
        {
            try
            {
                await Database.CanConnectAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Database health check failed");
                return false;
            }
        }

        public async Task<int> GetProjectCountAsync()
        {
            return await Projects.CountAsync();
        }

        public async Task<List<Project>> GetProjectsPagedAsync(int page, int pageSize)
        {
            return await Projects
                .OrderByDescending(p => p.LastModified)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Include(p => p.Tags)
                .ToListAsync();
        }

        public async Task<List<Project>> SearchProjectsAsync(string searchTerm)
        {
            return await Projects
                .Where(p => p.Name.Contains(searchTerm) ||
                           p.Path.Contains(searchTerm) ||
                           p.ProjectType.Contains(searchTerm))
                .Include(p => p.Tags)
                .ToListAsync();
        }

        public async Task<Project?> GetProjectByPathAsync(string path)
        {
            return await Projects
                .Include(p => p.Tags)
                .FirstOrDefaultAsync(p => p.Path == path);
        }

        public async Task<bool> ProjectExistsAsync(string path)
        {
            return await Projects.AnyAsync(p => p.Path == path);
        }
    }
}