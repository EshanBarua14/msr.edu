using Microsoft.EntityFrameworkCore;
using MsrEdu.Api.Data;

namespace MsrEdu.Api.Services;

/// <summary>
/// Content access rules (blueprint 2.D), checked in order:
/// free preview, free course (signed in), purchase, subscription covering the course, staff.
/// Pending or failed payments never create entitlements, so they never pass.
/// </summary>
public class AccessService(AppDbContext db, TimeProvider clock)
{
    public async Task<bool> HasCourseAccessAsync(Guid? userId, Guid courseId, CancellationToken ct = default)
    {
        if (userId is null) return false;
        var now = clock.GetUtcNow();
        var categoryId = await db.Courses.Where(c => c.Id == courseId).Select(c => c.CategoryId).FirstOrDefaultAsync(ct);

        return await db.Entitlements.AnyAsync(e =>
            e.UserId == userId
            && e.Status == EntitlementStatus.Active
            && e.StartsAt <= now
            && (e.ExpiresAt == null || e.ExpiresAt > now)
            && (e.CourseId == courseId
                || (e.PlanId != null && db.SubscriptionPlans.Any(p => p.Id == e.PlanId && (
                    p.AllCourses
                    || db.PlanCourses.Any(pc => pc.PlanId == p.Id && pc.CourseId == courseId)
                    || (categoryId != null && db.PlanCategories.Any(pc => pc.PlanId == p.Id && pc.CategoryId == categoryId)))))),
            ct);
    }

    /// <summary>Whether the viewer may read the content of an item in a course.</summary>
    public async Task<bool> CanViewAsync(Guid? userId, bool isStaff, Guid courseId, PublishStatus itemStatus, bool isPreview, CancellationToken ct = default)
    {
        if (userId is not null && isStaff) return true;
        var course = await db.Courses.Where(c => c.Id == courseId).Select(c => new { c.Status, c.IsFree }).FirstOrDefaultAsync(ct);
        if (course is null || course.Status != PublishStatus.Published || itemStatus != PublishStatus.Published) return false;
        if (isPreview) return true;
        if (userId is not null && course.IsFree) return true;
        return await HasCourseAccessAsync(userId, courseId, ct);
    }

    public async Task<bool> CanViewLessonAsync(Guid? userId, bool isStaff, Guid lessonId, CancellationToken ct = default)
    {
        var lesson = await db.Lessons.Where(l => l.Id == lessonId)
            .Select(l => new { l.CourseId, l.Status, l.IsPreview }).FirstOrDefaultAsync(ct);
        return lesson is not null && await CanViewAsync(userId, isStaff, lesson.CourseId, lesson.Status, lesson.IsPreview, ct);
    }

    public async Task<bool> CanViewQuizAsync(Guid? userId, bool isStaff, Guid quizId, CancellationToken ct = default)
    {
        var quiz = await db.Quizzes.Where(q => q.Id == quizId)
            .Select(q => new { q.CourseId, q.Status, q.IsPreview }).FirstOrDefaultAsync(ct);
        return quiz is not null && await CanViewAsync(userId, isStaff, quiz.CourseId, quiz.Status, quiz.IsPreview, ct);
    }

    public async Task<bool> CanViewResourceAsync(Guid? userId, bool isStaff, Guid resourceId, CancellationToken ct = default)
    {
        var r = await db.Resources.Where(x => x.Id == resourceId)
            .Select(x => new { x.CourseId, x.LessonId, x.Status, x.IsPreview }).FirstOrDefaultAsync(ct);
        if (r is null) return false;
        if (await CanViewAsync(userId, isStaff, r.CourseId, r.Status, r.IsPreview, ct)) return true;
        // A published resource attached to a viewable lesson inherits the lesson's preview access.
        return r.Status == PublishStatus.Published && r.LessonId is { } lessonId
            && await CanViewLessonAsync(userId, isStaff, lessonId, ct);
    }
}
