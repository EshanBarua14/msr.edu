namespace MsrEdu.Api.Data.Entities;

public class Category
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int Position { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Course
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Slug { get; set; }
    public required string Title { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? CoverUrl { get; set; }
    public Guid? CategoryId { get; set; }
    public PublishStatus Status { get; set; } = PublishStatus.Draft;
    /// <summary>Entire course free for signed-in users.</summary>
    public bool IsFree { get; set; }
    public int Position { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Category? Category { get; set; }
    public List<Module> Modules { get; set; } = [];
    public List<Lesson> Lessons { get; set; } = [];
}

public class Module
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CourseId { get; set; }
    public required string Title { get; set; }
    public int Position { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Lesson metadata (syllabus). The protected body is <see cref="LessonContent"/>.</summary>
public class Lesson
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CourseId { get; set; }
    public Guid? ModuleId { get; set; }
    public required string Slug { get; set; }
    public required string Title { get; set; }
    public string? Summary { get; set; }
    public LessonKind Kind { get; set; } = LessonKind.Text;
    public int? DurationSeconds { get; set; }
    /// <summary>Free preview: viewable by anyone, including anonymous visitors.</summary>
    public bool IsPreview { get; set; }
    public PublishStatus Status { get; set; } = PublishStatus.Draft;
    public int Position { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Course? Course { get; set; }
    public LessonContent? Content { get; set; }
}

public class LessonContent
{
    public Guid LessonId { get; set; }
    public string? Body { get; set; }
    public string? VideoRef { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Resource
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CourseId { get; set; }
    public Guid? LessonId { get; set; }
    public required string Name { get; set; }
    public string? MimeType { get; set; }
    public long? SizeBytes { get; set; }
    public bool IsPreview { get; set; }
    /// <summary>Sync and publish are separate actions.</summary>
    public DateTimeOffset? SyncedAt { get; set; }
    public PublishStatus Status { get; set; } = PublishStatus.Draft;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Course? Course { get; set; }
    public ResourceSource? Source { get; set; }
}

/// <summary>Drive linkage. Never returned to learners.</summary>
public class ResourceSource
{
    public Guid ResourceId { get; set; }
    public required string DriveFileId { get; set; }
    public DateTimeOffset? DriveModifiedAt { get; set; }
    public string? Checksum { get; set; }
}

public class Enrollment
{
    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? LastLessonId { get; set; }
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
}

public class LessonProgress
{
    public Guid UserId { get; set; }
    public Guid LessonId { get; set; }
    public short ProgressPct { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Quiz
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CourseId { get; set; }
    public Guid? LessonId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public short PassScorePct { get; set; } = 60;
    public int? TimeLimitSeconds { get; set; }
    public int? MaxAttempts { get; set; }
    public bool IsPreview { get; set; }
    public PublishStatus Status { get; set; } = PublishStatus.Draft;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Course? Course { get; set; }
    public List<Question> Questions { get; set; } = [];
}

public class Question
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid QuizId { get; set; }
    public required string Prompt { get; set; }
    /// <summary>Shown only after an attempt is submitted.</summary>
    public string? Explanation { get; set; }
    public decimal Marks { get; set; } = 1;
    public int Position { get; set; }
    /// <summary>AI-generated questions start as draft and must be approved by a person.</summary>
    public bool AiGenerated { get; set; }
    public QuestionReviewStatus ReviewStatus { get; set; } = QuestionReviewStatus.Draft;
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<QuestionOption> Options { get; set; } = [];
}

public class QuestionOption
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid QuestionId { get; set; }
    public required string Label { get; set; }
    /// <summary>Never serialised to learners before submission.</summary>
    public bool IsCorrect { get; set; }
    public int Position { get; set; }
}

public class QuizAttempt
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid QuizId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAt { get; set; }
    public decimal? Score { get; set; }
    public decimal? MaxScore { get; set; }
    public bool? Passed { get; set; }
    public List<QuizAnswer> Answers { get; set; } = [];
}

public class QuizAnswer
{
    public Guid AttemptId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid? OptionId { get; set; }
    public bool? IsCorrect { get; set; }
}
