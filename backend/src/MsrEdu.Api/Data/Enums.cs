namespace MsrEdu.Api.Data;

// Mapped to PostgreSQL enum types; labels are snake_cased (RefundPending -> refund_pending).

public enum PublishStatus { Draft, Published, Archived }
public enum LessonKind { Video, Document, Text, Quiz }
public enum QuestionReviewStatus { Draft, Approved, Rejected }

/// <summary>Shared by orders and payment transactions. Transitions: <see cref="Services.PaymentStateMachine"/>.</summary>
public enum PaymentState
{
    Created, Pending, Processing, Successful, Failed, Cancelled, Expired,
    RefundPending, PartiallyRefunded, Refunded,
}

public enum PaymentMethod { Bkash, Nagad, Card, BankTransfer, Other }
public enum BillingPeriod { Month, Year, CustomDays }
public enum RenewalMode { Manual, Auto }
public enum SaleStatus { Active, Inactive, Archived }
public enum ProductKind { Course, SubscriptionPlan }
public enum SubscriptionStatus { Pending, Active, Grace, Expired, Cancelled }
public enum EntitlementSource { Purchase, Subscription, ManualGrant, Promotion }
public enum EntitlementStatus { Active, Expired, Suspended, Revoked }
public enum RefundStatus { Requested, Approved, Rejected, Processing, Completed, Failed }
public enum ReviewStatus { Submitted, Approved, Rejected }
public enum ReconciliationStatus { Open, Investigating, Resolved, Ignored }
public enum DiscountKind { Percent, Fixed }
