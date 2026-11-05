using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Strategy.Modules.Portfolio.Domain;

namespace Strategy.Modules.Portfolio.Infrastructure;

// Wasel and Portfolio completion tables (docs/design/portfolio.md §48, §58, §59).

internal sealed class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> builder)
    {
        builder.ToTable("vendor");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).HasMaxLength(50);
        ChildConfiguration.Texts(builder, 300, nameof(Vendor.NameAr), nameof(Vendor.NameEn), nameof(Vendor.ContactPerson), nameof(Vendor.Email));
        ChildConfiguration.Texts(builder, 100, nameof(Vendor.TradeLicenceNo), nameof(Vendor.Phone));
        ChildConfiguration.Texts(builder, 20, nameof(Vendor.Status), nameof(Vendor.CountryCode));
        ChildConfiguration.Texts(builder, 2000, nameof(Vendor.Address), nameof(Vendor.StatusReason), nameof(Vendor.Notes));
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => x.TradeLicenceNo).IsUnique().HasFilter("trade_licence_no IS NOT NULL");
    }
}

internal sealed class VendorUserConfiguration : IEntityTypeConfiguration<VendorUser>
{
    public void Configure(EntityTypeBuilder<VendorUser> builder)
    {
        builder.ToTable("vendor_user");
        builder.Property(x => x.Id).ValueGeneratedNever();
        ChildConfiguration.Texts(builder, 300, nameof(VendorUser.Email), nameof(VendorUser.Name));
        ChildConfiguration.Texts(builder, 20, nameof(VendorUser.Status), nameof(VendorUser.Language));
        builder.Property(x => x.DisabledReason).HasMaxLength(2000);
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasIndex(x => x.VendorId);
    }
}

internal sealed class VendorAssignmentConfiguration : IEntityTypeConfiguration<VendorAssignment>
{
    public void Configure(EntityTypeBuilder<VendorAssignment> builder)
    {
        builder.ToTable("vendor_assignment");
        builder.Property(x => x.Id).ValueGeneratedNever();
        ChildConfiguration.Texts(builder, 20, nameof(VendorAssignment.Status), nameof(VendorAssignment.ExpiryRule));
        builder.Property(x => x.EndedReason).HasMaxLength(2000);
        builder.HasIndex(x => new { x.ProjectId, x.VendorId }).IsUnique().HasFilter("status = 'active'");
        builder.HasIndex(x => x.VendorId);
    }
}

internal sealed class VendorMemberConfiguration : IEntityTypeConfiguration<VendorMember>
{
    public void Configure(EntityTypeBuilder<VendorMember> builder)
    {
        builder.ToTable("vendor_member");
        builder.Property(x => x.Id).ValueGeneratedNever();
        ChildConfiguration.Texts(builder, 40, nameof(VendorMember.Status), nameof(VendorMember.Role));
        builder.Property(x => x.RevokedReason).HasMaxLength(2000);
        builder.HasIndex(x => x.ProjectId);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.AssignmentId);
    }
}

internal sealed class PlanProposalConfiguration : IEntityTypeConfiguration<PlanProposal>
{
    public void Configure(EntityTypeBuilder<PlanProposal> builder)
    {
        builder.ToTable("plan_proposal");
        builder.Property(x => x.Id).ValueGeneratedNever();
        ChildConfiguration.Texts(builder, 20, nameof(PlanProposal.Status), nameof(PlanProposal.Kind));
        ChildConfiguration.Texts(builder, 4000, nameof(PlanProposal.VendorNote), nameof(PlanProposal.PmComment));
        foreach (var name in new[] { nameof(PlanProposal.Items), nameof(PlanProposal.AcceptedItems), nameof(PlanProposal.Diff) })
        {
            builder.Property(name).HasColumnType("jsonb");
        }

        builder.HasIndex(x => new { x.ProjectId, x.VendorId, x.Sequence }).IsUnique();
    }
}

internal sealed class VendorSubmissionConfiguration : IEntityTypeConfiguration<VendorSubmission>
{
    public void Configure(EntityTypeBuilder<VendorSubmission> builder)
    {
        builder.ToTable("vendor_submission");
        builder.Property(x => x.Id).ValueGeneratedNever();
        ChildConfiguration.Texts(builder, 20, nameof(VendorSubmission.Status), nameof(VendorSubmission.SharedOutcome), nameof(VendorSubmission.Period));
        builder.Property(x => x.SharedNote).HasMaxLength(4000);
        builder.HasIndex(x => new { x.MonthlyUpdateId, x.VendorId }).IsUnique();
        builder.HasIndex(x => x.ProjectId);
    }
}

internal sealed class VendorSubmissionItemConfiguration : IEntityTypeConfiguration<VendorSubmissionItem>
{
    public void Configure(EntityTypeBuilder<VendorSubmissionItem> builder)
    {
        builder.ToTable("vendor_submission_item");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ReviewStatus).HasMaxLength(20);
        ChildConfiguration.Texts(builder, MilestoneRules.TextMaxLength, nameof(VendorSubmissionItem.ProgressNote), nameof(VendorSubmissionItem.Causality),
            nameof(VendorSubmissionItem.CorrectiveActions), nameof(VendorSubmissionItem.FinalCausality), nameof(VendorSubmissionItem.FinalCorrectiveActions),
            nameof(VendorSubmissionItem.PmComment));
        builder.Property(x => x.Achieve).HasPrecision(9, 4);
        builder.Property(x => x.FinalAchieve).HasPrecision(9, 4);
        builder.Property(x => x.Previous).HasColumnType("jsonb");
        builder.Property(x => x.Diff).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.SubmissionId, x.MilestoneId }).IsUnique();
        builder.HasIndex(x => x.ProjectId);
    }
}

internal sealed class VendorDeliverableConfiguration : IEntityTypeConfiguration<VendorDeliverable>
{
    public void Configure(EntityTypeBuilder<VendorDeliverable> builder)
    {
        builder.ToTable("vendor_deliverable");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Title).HasMaxLength(300);
        ChildConfiguration.Texts(builder, 4000, nameof(VendorDeliverable.Description), nameof(VendorDeliverable.PmComment));
        builder.HasIndex(x => x.ProjectId);
    }
}

internal sealed class VendorItemConfiguration : IEntityTypeConfiguration<VendorItem>
{
    public void Configure(EntityTypeBuilder<VendorItem> builder)
    {
        builder.ToTable("vendor_item");
        builder.Property(x => x.Id).ValueGeneratedNever();
        ChildConfiguration.Texts(builder, 20, nameof(VendorItem.Status), nameof(VendorItem.Kind));
        builder.Property(x => x.Title).HasMaxLength(300);
        ChildConfiguration.Texts(builder, 4000, nameof(VendorItem.Description), nameof(VendorItem.Impact), nameof(VendorItem.PmComment));
        builder.HasIndex(x => x.ProjectId);
    }
}

internal sealed class VendorCommentConfiguration : IEntityTypeConfiguration<VendorComment>
{
    public void Configure(EntityTypeBuilder<VendorComment> builder)
    {
        builder.ToTable("vendor_comment");
        builder.Property(x => x.Id).ValueGeneratedNever();
        ChildConfiguration.Texts(builder, 20, nameof(VendorComment.SubjectType), nameof(VendorComment.AuthorKind));
        builder.Property(x => x.AuthorName).HasMaxLength(300);
        builder.Property(x => x.Body).HasMaxLength(VendorComment.MaxLength);
        builder.HasIndex(x => new { x.ProjectId, x.SubjectType, x.SubjectId });
    }
}

internal sealed class VendorRatingConfiguration : IEntityTypeConfiguration<VendorRating>
{
    public void Configure(EntityTypeBuilder<VendorRating> builder)
    {
        builder.ToTable("vendor_rating");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Overall).HasPrecision(5, 2);
        builder.Property(x => x.Comment).HasMaxLength(4000);
        builder.HasIndex(x => x.VendorId);
        builder.HasIndex(x => x.ProjectId);
    }
}

internal sealed class PortfolioReviewConfiguration : IEntityTypeConfiguration<PortfolioReview>
{
    public void Configure(EntityTypeBuilder<PortfolioReview> builder)
    {
        builder.ToTable("portfolio_review");
        builder.Property(x => x.Id).ValueGeneratedNever();
        ChildConfiguration.Texts(builder, 20, nameof(PortfolioReview.Period), nameof(PortfolioReview.Status));
        builder.Property(x => x.Title).HasMaxLength(300);
        builder.Property(x => x.Notes).HasMaxLength(4000);
        builder.HasIndex(x => x.Period);
    }
}

internal sealed class ReviewDecisionConfiguration : IEntityTypeConfiguration<ReviewDecision>
{
    public void Configure(EntityTypeBuilder<ReviewDecision> builder)
    {
        builder.ToTable("portfolio_review_decision");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Decision).HasMaxLength(20);
        builder.Property(x => x.Comment).HasMaxLength(4000);
        builder.HasIndex(x => new { x.ReviewId, x.ProjectId }).IsUnique();
    }
}
