using Microsoft.EntityFrameworkCore;
using MsrEdu.Api.Data.Entities;

namespace MsrEdu.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LessonContent> LessonContents => Set<LessonContent>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ResourceSource> ResourceSources => Set<ResourceSource>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<QuizAnswer> QuizAnswers => Set<QuizAnswer>();

    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<PlanCourse> PlanCourses => Set<PlanCourse>();
    public DbSet<PlanCategory> PlanCategories => Set<PlanCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Discount> Discounts => Set<Discount>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<BankPaymentSubmission> BankPaymentSubmissions => Set<BankPaymentSubmission>();
    public DbSet<ReconciliationRecord> ReconciliationRecords => Set<ReconciliationRecord>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Entitlement> Entitlements => Set<Entitlement>();

    /// <summary>Registers PostgreSQL enum types. Shared by the app and design-time tooling.</summary>
    public static void MapEnums(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder o)
    {
        o.MapEnum<PublishStatus>("publish_status");
        o.MapEnum<LessonKind>("lesson_kind");
        o.MapEnum<QuestionReviewStatus>("question_review_status");
        o.MapEnum<PaymentState>("payment_state");
        o.MapEnum<PaymentMethod>("payment_method");
        o.MapEnum<BillingPeriod>("billing_period");
        o.MapEnum<RenewalMode>("renewal_mode");
        o.MapEnum<SaleStatus>("sale_status");
        o.MapEnum<ProductKind>("product_kind");
        o.MapEnum<SubscriptionStatus>("subscription_status");
        o.MapEnum<EntitlementSource>("entitlement_source");
        o.MapEnum<EntitlementStatus>("entitlement_status");
        o.MapEnum<RefundStatus>("refund_status");
        o.MapEnum<ReviewStatus>("review_status");
        o.MapEnum<ReconciliationStatus>("reconciliation_status");
        o.MapEnum<DiscountKind>("discount_kind");
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("pgcrypto");
        const string slug = "~ '^[a-z0-9-]+$'";
        const string currency = "~ '^[A-Z]{3}$'";

        // ---------------------------------------------------------------- identity
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.GoogleSubject).IsUnique();
            e.HasMany(x => x.Roles).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Role>(e =>
        {
            e.HasKey(x => x.Key);
            e.ToTable(t => t.HasCheckConstraint("ck_roles_key", "key ~ '^[a-z_]+$'"));
            e.HasData(
                new Role { Key = RoleKeys.OwnerAdmin, Name = "Owner / Admin", Description = "Full system configuration and access management" },
                new Role { Key = RoleKeys.ContentManager, Name = "Content Manager", Description = "Manage courses, lessons and learning materials" },
                new Role { Key = RoleKeys.FinanceManager, Name = "Finance / Payment Manager", Description = "Review transactions, refunds and financial reports" },
                new Role { Key = RoleKeys.SupportStaff, Name = "Support Staff", Description = "Limited user and transaction lookup for support" },
                new Role { Key = RoleKeys.Learner, Name = "Learner", Description = "Default role for every registered user" });
        });
        b.Entity<UserRole>(e =>
        {
            e.HasKey(x => new { x.UserId, x.RoleKey });
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleKey).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.GrantedBy).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<AppSetting>(e => e.HasKey(x => x.Key));
        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.CreatedAt);
        });

        // ---------------------------------------------------------------- learning
        b.Entity<Category>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_categories_slug", $"slug {slug}"));
        });
        b.Entity<Course>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_courses_slug", $"slug {slug}"));
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Modules).WithOne().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Lessons).WithOne(x => x.Course).HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Module>(e => e.HasIndex(x => new { x.CourseId, x.Position }));
        b.Entity<Lesson>(e =>
        {
            e.HasIndex(x => new { x.CourseId, x.Slug }).IsUnique();
            e.HasIndex(x => new { x.ModuleId, x.Position });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_lessons_slug", $"slug {slug}");
                t.HasCheckConstraint("ck_lessons_duration", "duration_seconds >= 0");
            });
            e.HasOne<Module>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Content).WithOne().HasForeignKey<LessonContent>(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<LessonContent>(e => e.HasKey(x => x.LessonId));
        b.Entity<Resource>(e =>
        {
            e.HasIndex(x => x.LessonId);
            e.ToTable(t => t.HasCheckConstraint("ck_resources_size", "size_bytes >= 0"));
            e.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Lesson>().WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Source).WithOne().HasForeignKey<ResourceSource>(x => x.ResourceId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ResourceSource>(e =>
        {
            e.HasKey(x => x.ResourceId);
            e.HasIndex(x => x.DriveFileId).IsUnique();
        });
        b.Entity<Enrollment>(e =>
        {
            e.HasKey(x => new { x.UserId, x.CourseId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Lesson>().WithMany().HasForeignKey(x => x.LastLessonId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<LessonProgress>(e =>
        {
            e.HasKey(x => new { x.UserId, x.LessonId });
            e.ToTable(t => t.HasCheckConstraint("ck_lesson_progress_pct", "progress_pct between 0 and 100"));
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Lesson>().WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Quiz>(e =>
        {
            e.HasIndex(x => x.CourseId);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_quizzes_pass_score", "pass_score_pct between 0 and 100");
                t.HasCheckConstraint("ck_quizzes_time_limit", "time_limit_seconds > 0");
                t.HasCheckConstraint("ck_quizzes_max_attempts", "max_attempts > 0");
            });
            e.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Lesson>().WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Questions).WithOne().HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Question>(e =>
        {
            e.HasIndex(x => new { x.QuizId, x.Position });
            e.Property(x => x.Marks).HasPrecision(6, 2);
            e.ToTable(t => t.HasCheckConstraint("ck_questions_marks", "marks > 0"));
            e.HasMany(x => x.Options).WithOne().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<QuestionOption>(e => e.HasIndex(x => new { x.QuestionId, x.Position }));
        b.Entity<QuizAttempt>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.QuizId });
            e.Property(x => x.Score).HasPrecision(8, 2);
            e.Property(x => x.MaxScore).HasPrecision(8, 2);
            e.HasOne<Quiz>().WithMany().HasForeignKey(x => x.QuizId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Answers).WithOne().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<QuizAnswer>(e =>
        {
            e.HasKey(x => new { x.AttemptId, x.QuestionId });
            e.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<QuestionOption>().WithMany().HasForeignKey(x => x.OptionId).OnDelete(DeleteBehavior.SetNull);
        });

        // ---------------------------------------------------------------- commerce: catalogue
        b.Entity<SubscriptionPlan>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_plans_slug", $"slug {slug}");
                t.HasCheckConstraint("ck_plans_price", "price_minor >= 0");
                t.HasCheckConstraint("ck_plans_currency", $"currency {currency}");
                t.HasCheckConstraint("ck_plans_period_days", "period_days > 0 and (billing_period <> 'custom_days' or period_days is not null)");
                t.HasCheckConstraint("ck_plans_grace", "grace_days >= 0");
                t.HasCheckConstraint("ck_plans_window", "available_until is null or available_from is null or available_until > available_from");
            });
        });
        b.Entity<PlanCourse>(e =>
        {
            e.HasKey(x => new { x.PlanId, x.CourseId });
            e.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PlanCategory>(e =>
        {
            e.HasKey(x => new { x.PlanId, x.CategoryId });
            e.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Product>(e =>
        {
            e.HasIndex(x => x.CourseId).IsUnique();
            e.HasIndex(x => x.PlanId).IsUnique();
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_products_price", "price_minor >= 0 and (promo_price_minor is null or (promo_price_minor >= 0 and promo_price_minor < price_minor))");
                t.HasCheckConstraint("ck_products_currency", $"currency {currency}");
                t.HasCheckConstraint("ck_products_scope",
                    "(kind = 'course' and course_id is not null and plan_id is null) or " +
                    "(kind = 'subscription_plan' and plan_id is not null and course_id is null and access_days is null)");
                t.HasCheckConstraint("ck_products_access_days", "access_days > 0");
                t.HasCheckConstraint("ck_products_promo_window", "promo_ends_at is null or promo_starts_at is null or promo_ends_at > promo_starts_at");
            });
        });
        b.Entity<Discount>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_discounts_code", "code = upper(code) and code ~ '^[A-Z0-9_-]{3,32}$'");
                t.HasCheckConstraint("ck_discounts_value", "value > 0 and (kind <> 'percent' or value <= 100) and (kind <> 'fixed' or currency is not null)");
                t.HasCheckConstraint("ck_discounts_redemptions", "redemptions >= 0 and (max_redemptions is null or (max_redemptions > 0 and redemptions <= max_redemptions))");
            });
        });

        // ---------------------------------------------------------------- commerce: orders and payments
        b.Entity<Order>(e =>
        {
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => x.Status);
            e.Property(x => x.Reference).HasDefaultValueSql(
                "'MSR-' || to_char(now() at time zone 'utc', 'YYMMDD') || '-' || upper(encode(gen_random_bytes(4), 'hex'))");
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Discount>().WithMany().HasForeignKey(x => x.DiscountId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_orders_amounts", "subtotal_minor >= 0 and discount_minor >= 0 and total_minor >= 0 and total_minor = subtotal_minor - discount_minor");
                t.HasCheckConstraint("ck_orders_currency", $"currency {currency}");
            });
        });
        b.Entity<OrderItem>(e =>
        {
            e.HasIndex(x => new { x.OrderId, x.ProductId }).IsUnique();
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_order_items_quantity", "quantity = 1");
                t.HasCheckConstraint("ck_order_items_total", "unit_price_minor >= 0 and line_total_minor = unit_price_minor * quantity");
            });
        });
        b.Entity<PaymentTransaction>(e =>
        {
            e.HasIndex(x => x.OrderId);
            e.HasIndex(x => new { x.Provider, x.ProviderTxnId }).IsUnique();
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_payment_transactions_provider", "provider ~ '^[a-z0-9_]+$'");
                t.HasCheckConstraint("ck_payment_transactions_amount", "amount_minor >= 0");
                t.HasCheckConstraint("ck_payment_transactions_verified",
                    "status <> 'successful' or (verified_at is not null and provider_txn_id is not null)");
            });
        });
        b.Entity<PaymentEvent>(e =>
        {
            e.HasIndex(x => new { x.Provider, x.ProviderEventId }).IsUnique();
            e.HasOne<PaymentTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Refund>(e =>
        {
            e.HasIndex(x => new { x.TransactionId, x.ProviderRefundId }).IsUnique();
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PaymentTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_refunds_amount", "amount_minor > 0"));
        });
        b.Entity<Receipt>(e =>
        {
            e.HasIndex(x => x.ReceiptNumber).IsUnique();
            e.HasIndex(x => x.OrderId).IsUnique();
            e.Property(x => x.ReceiptNumber).HasDefaultValueSql(
                "'R-' || to_char(now() at time zone 'utc', 'YYYY') || '-' || lpad(nextval('receipt_seq')::text, 6, '0')");
            e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PaymentTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.HasSequence("receipt_seq");
        b.Entity<BankPaymentSubmission>(e =>
        {
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_bank_submissions_amount", "amount_minor >= 0"));
        });
        b.Entity<ReconciliationRecord>(e =>
        {
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PaymentTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---------------------------------------------------------------- access
        b.Entity<Subscription>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Status });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Order>().WithMany().HasForeignKey(x => x.SourceOrderId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_subscriptions_period", "current_period_end is null or current_period_end > current_period_start"));
        });
        b.Entity<Entitlement>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.SourceOrderItemId).IsUnique();
            e.HasIndex(x => new { x.SubscriptionId, x.StartsAt }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OrderItem>().WithMany().HasForeignKey(x => x.SourceOrderItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Subscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.GrantedBy).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_entitlements_scope", "(course_id is null) <> (plan_id is null)");
                t.HasCheckConstraint("ck_entitlements_window", "expires_at is null or expires_at > starts_at");
                t.HasCheckConstraint("ck_entitlements_purchase", "source <> 'purchase' or source_order_item_id is not null");
                t.HasCheckConstraint("ck_entitlements_subscription", "source <> 'subscription' or (subscription_id is not null and plan_id is not null)");
                t.HasCheckConstraint("ck_entitlements_grant", "source not in ('manual_grant', 'promotion') or (granted_by is not null and grant_reason is not null)");
                t.HasCheckConstraint("ck_entitlements_reason", "status in ('active', 'expired') or status_reason is not null");
            });
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            var updated = entry.Metadata.FindProperty("UpdatedAt");
            if (updated is not null) entry.Property(updated.Name).CurrentValue = now;
        }
        return base.SaveChangesAsync(ct);
    }
}
