using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TravelOS.PlatformApi.Domain;

namespace TravelOS.PlatformApi.Data;

public sealed class TravelOsDbContext(DbContextOptions<TravelOsDbContext> options)
    : IdentityDbContext<User, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripMember> TripMembers => Set<TripMember>();
    public DbSet<Itinerary> Itineraries => Set<Itinerary>();
    public DbSet<ItineraryDay> ItineraryDays => Set<ItineraryDay>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<GeneratedItineraryDraft> GeneratedItineraryDrafts => Set<GeneratedItineraryDraft>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetItem> BudgetItems => Set<BudgetItem>();
    public DbSet<TripInvitation> TripInvitations => Set<TripInvitation>();
    public DbSet<TripMemberPreference> TripMemberPreferences => Set<TripMemberPreference>();
    public DbSet<GroupPoll> GroupPolls => Set<GroupPoll>();
    public DbSet<GroupPollOption> GroupPollOptions => Set<GroupPollOption>();
    public DbSet<GroupPollVote> GroupPollVotes => Set<GroupPollVote>();
    public DbSet<TripExpense> TripExpenses => Set<TripExpense>();
    public DbSet<TripExpenseShare> TripExpenseShares => Set<TripExpenseShare>();
    public DbSet<TripRepayment> TripRepayments => Set<TripRepayment>();
    public DbSet<TripExpenseAudit> TripExpenseAudits => Set<TripExpenseAudit>();
    public DbSet<TripReceipt> TripReceipts => Set<TripReceipt>();
    public DbSet<TripExpenseConversion> TripExpenseConversions => Set<TripExpenseConversion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<Trip>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Destination).HasMaxLength(160).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.ToTable(t => t.HasCheckConstraint("CK_Trips_DateRange", "\"EndDate\" >= \"StartDate\""));
        });

        modelBuilder.Entity<TripMember>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Role).HasMaxLength(24).IsRequired();
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.TripId, x.UserId }).IsUnique();
        });

        modelBuilder.Entity<Itinerary>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TripId, x.Version }).IsUnique();
            entity.ToTable(t => t.HasCheckConstraint("CK_Itineraries_Version", "\"Version\" > 0"));
            entity.HasOne<Trip>().WithMany().HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Days).WithOne().HasForeignKey(x => x.ItineraryId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ApprovedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ItineraryDay>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ItineraryId, x.DayNumber }).IsUnique();
            entity.HasMany(x => x.Activities).WithOne().HasForeignKey(x => x.ItineraryDayId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Activity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.PlaceName).HasMaxLength(200);
            entity.Property(x => x.Latitude).HasPrecision(9, 6);
            entity.Property(x => x.Longitude).HasPrecision(9, 6);
            entity.Property(x => x.EstimatedCost).HasPrecision(19, 4);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3);
        });

        modelBuilder.Entity<GeneratedItineraryDraft>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ContractVersion).HasMaxLength(16).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
            entity.HasOne<Trip>().WithMany().HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.RequestedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TripInvitation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
            entity.HasOne<Trip>().WithMany().HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.InvitedUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.InvitedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.TripId, x.InvitedUserId, x.Status });
        });

        modelBuilder.Entity<TripExpense>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.Property(x => x.Amount).HasPrecision(19, 4);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.HasOne<Trip>().WithMany().HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.PaidByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Shares).WithOne().HasForeignKey(x => x.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.TripId, x.ExpenseDate });
        });

        modelBuilder.Entity<TripExpenseShare>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ShareAmount).HasPrecision(19, 4);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ExpenseId, x.UserId }).IsUnique();
        });

        modelBuilder.Entity<TripRepayment>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(19, 4);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.HasOne<Trip>().WithMany().HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.FromUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ToUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.TripId, x.Status, x.CurrencyCode });
            entity.HasIndex(x => new { x.FromUserId, x.ToUserId, x.Status });
        });

        modelBuilder.Entity<TripExpenseAudit>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(24).IsRequired();
            entity.Property(x => x.BeforeJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.AfterJson).HasColumnType("jsonb").IsRequired();
            entity.HasOne<Trip>().WithMany().HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ChangedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            // ExpenseId intentionally has no FK so audit history survives expense removal.
            entity.HasIndex(x => new { x.TripId, x.ExpenseId, x.ChangedAt });
        });

        modelBuilder.Entity<TripReceipt>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.StoredFileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ExtractionStatus).HasMaxLength(24).IsRequired();
            entity.Property(x => x.MerchantName).HasMaxLength(180);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3);
            entity.Property(x => x.TotalAmount).HasPrecision(19, 4);
            entity.Property(x => x.Confidence).HasPrecision(5, 4);
            entity.Property(x => x.ExtractionWarning).HasMaxLength(500);
            entity.HasOne<Trip>().WithMany().HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<TripExpense>().WithMany().HasForeignKey(x => x.ExpenseId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ConfirmedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.TripId, x.Sha256 });
            entity.HasIndex(x => new { x.TripId, x.ExpenseId, x.UploadedAt });
        });

        modelBuilder.Entity<TripExpenseConversion>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OriginalAmount).HasPrecision(19, 4);
            entity.Property(x => x.ConvertedAmount).HasPrecision(19, 4);
            entity.Property(x => x.ExchangeRate).HasPrecision(28, 12);
            entity.Property(x => x.OriginalCurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.ConvertedCurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Provider).HasMaxLength(120).IsRequired();
            entity.HasOne<Trip>().WithMany().HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.RequestedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            // ExpenseId intentionally has no FK so the conversion audit survives expense deletion.
            entity.HasIndex(x => new { x.TripId, x.ExpenseId, x.CreatedAt });
        });

        modelBuilder.Entity<GroupPoll>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.Status).HasMaxLength(16).IsRequired();
            entity.HasOne<Trip>().WithMany().HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Options).WithOne().HasForeignKey(x => x.PollId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.TripId, x.CreatedAt });
        });

        modelBuilder.Entity<GroupPollOption>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Label).HasMaxLength(160).IsRequired();
            entity.HasIndex(x => new { x.PollId, x.SortOrder }).IsUnique();
        });

        modelBuilder.Entity<GroupPollVote>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne<GroupPoll>().WithMany(x => x.Votes).HasForeignKey(x => x.PollId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<GroupPollOption>().WithMany().HasForeignKey(x => x.OptionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.PollId, x.UserId }).IsUnique();
        });

        modelBuilder.Entity<TripMemberPreference>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.InterestsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.Pace).HasMaxLength(24).IsRequired();
            entity.Property(x => x.TravelStyle).HasMaxLength(32).IsRequired();
            entity.Property(x => x.DailyBudgetAmount).HasPrecision(19, 4);
            entity.Property(x => x.DailyBudgetCurrencyCode).HasMaxLength(3);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasOne<Trip>().WithMany().HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.TripId, x.UserId }).IsUnique();
        });

        modelBuilder.Entity<Budget>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Amount).HasPrecision(19, 4);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.HasOne<Trip>().WithOne().HasForeignKey<Budget>(x => x.TripId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BudgetItem>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Category).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(19, 4);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.HasOne<Budget>().WithMany(x => x.Items).HasForeignKey(x => x.BudgetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}
