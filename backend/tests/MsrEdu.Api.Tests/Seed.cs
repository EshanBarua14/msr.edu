using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MsrEdu.Api.Data;
using MsrEdu.Api.Data.Entities;

namespace MsrEdu.Api.Tests;

/// <summary>Builds catalogue, users and orders directly in the database, as server code would.</summary>
public sealed class Seed(AppDbContext db)
{
    public async Task<User> UserAsync(string email, params string[] roles)
    {
        var user = new User { Email = email, FullName = email.Split('@')[0] };
        user.Roles.Add(new UserRole { RoleKey = RoleKeys.Learner });
        foreach (var r in roles) user.Roles.Add(new UserRole { RoleKey = r });
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task<(Course Course, Lesson Preview, Lesson Paid)> CourseAsync(string slug, PublishStatus status = PublishStatus.Published, bool free = false)
    {
        var course = new Course { Slug = slug, Title = slug, Status = status, IsFree = free };
        var preview = new Lesson { CourseId = course.Id, Slug = "intro", Title = "Intro", IsPreview = true, Status = PublishStatus.Published, Content = new() { Body = "free body" } };
        var paid = new Lesson { CourseId = course.Id, Slug = "paid", Title = "Paid", Status = PublishStatus.Published, Content = new() { Body = "paid body" }, Position = 1 };
        db.Courses.Add(course);
        db.Lessons.AddRange(preview, paid);
        await db.SaveChangesAsync();
        return (course, preview, paid);
    }

    public async Task<Product> CourseProductAsync(Course course, long price = 150_000, int? accessDays = null)
    {
        var product = new Product { Kind = ProductKind.Course, CourseId = course.Id, Name = course.Title, PriceMinor = price, Status = SaleStatus.Active, AccessDays = accessDays };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    public async Task<(SubscriptionPlan Plan, Product Product)> PlanAsync(string slug, bool allCourses = true, int graceDays = 0)
    {
        var plan = new SubscriptionPlan { Slug = slug, Name = slug, PriceMinor = 50_000, BillingPeriod = BillingPeriod.Month, AllCourses = allCourses, GraceDays = graceDays, Status = SaleStatus.Active };
        var product = new Product { Kind = ProductKind.SubscriptionPlan, PlanId = plan.Id, Name = slug, PriceMinor = 50_000, Status = SaleStatus.Active };
        db.SubscriptionPlans.Add(plan);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return (plan, product);
    }

    /// <summary>A pending order with one transaction, as checkout would create it.</summary>
    public async Task<(Order Order, PaymentTransaction Txn)> PendingOrderAsync(User user, Product product, string txnId)
    {
        var order = new Order { UserId = user.Id, Currency = "BDT", SubtotalMinor = product.PriceMinor, TotalMinor = product.PriceMinor, Status = PaymentState.Pending };
        order.Items.Add(new OrderItem
        {
            ProductId = product.Id, UnitPriceMinor = product.PriceMinor, LineTotalMinor = product.PriceMinor, Currency = "BDT",
            ProductSnapshot = JsonSerializer.SerializeToDocument(new { product.Name, product.PriceMinor }),
        });
        var txn = new PaymentTransaction { OrderId = order.Id, Provider = "sandbox", Method = PaymentMethod.Bkash, ProviderTxnId = txnId, AmountMinor = product.PriceMinor, Currency = "BDT", Status = PaymentState.Pending };
        db.Orders.Add(order);
        db.PaymentTransactions.Add(txn);
        await db.SaveChangesAsync();
        return (order, txn);
    }

    /// <summary>What a verified webhook does before granting access.</summary>
    public async Task MarkPaidAsync(Order order, PaymentTransaction txn)
    {
        txn.Status = PaymentState.Successful;
        txn.VerifiedAt = DateTimeOffset.UtcNow;
        order.Status = PaymentState.Successful;
        order.PaidAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<Order> ReloadAsync(Order order) => await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
}
