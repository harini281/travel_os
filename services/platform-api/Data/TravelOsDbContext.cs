using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Data;

public sealed class TravelOsDbContext(DbContextOptions<TravelOsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripMember> TripMembers => Set<TripMember>();
    public DbSet<Itinerary> Itineraries => Set<Itinerary>();
    public DbSet<ItineraryDay> ItineraryDays => Set<ItineraryDay>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<GeneratedItineraryDraft> GeneratedItineraryDrafts => Set<GeneratedItineraryDraft>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetItem> BudgetItems => Set<BudgetItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<Trip>().Property(x => x.Name).HasMaxLength(160).IsRequired();
        modelBuilder.Entity<Budget>().Property(x => x.Amount).HasPrecision(19, 4);
        modelBuilder.Entity<BudgetItem>().Property(x => x.Amount).HasPrecision(19, 4);
        modelBuilder.Entity<TripMember>().HasIndex(x => new { x.TripId, x.UserId }).IsUnique();
        modelBuilder.Entity<ItineraryDay>().HasIndex(x => new { x.ItineraryId, x.DayNumber }).IsUnique();
        modelBuilder.Entity<Activity>().Property(x => x.EstimatedCost).HasPrecision(19, 4);
        modelBuilder.Entity<GeneratedItineraryDraft>().Property(x => x.Status).HasMaxLength(32);
        base.OnModelCreating(modelBuilder);
    }
}
