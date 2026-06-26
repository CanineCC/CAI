using Cai.Scoring;
using FluentValidation;

namespace Cai.Web;

/// <summary>Inbound validation (S1) for the public <c>/api/score</c> + <c>/api/verify</c> endpoints: reject a malformed
/// evidence bundle before it reaches the deterministic scorer. Mirrors the bundle's documented invariants — a rubric
/// version is required, and every dimension/meta score, confidence and coverage must sit in its valid range.</summary>
public sealed class EvidenceBundleValidator : AbstractValidator<EvidenceBundle>
{
    public EvidenceBundleValidator()
    {
        RuleFor(b => b.RubricVersion).NotEmpty().WithMessage("rubricVersion is required.");
        RuleFor(b => b.AnalyzableProjects).GreaterThanOrEqualTo(0);
        RuleFor(b => b.ProductionLoc).GreaterThanOrEqualTo(0);

        RuleForEach(b => b.Dimensions).ChildRules(d =>
        {
            d.RuleFor(x => x.Id).NotEmpty();
            d.RuleFor(x => x.Category).NotEmpty();
            d.RuleFor(x => x.ScoreZeroToTen).InclusiveBetween(0, 10);
            d.RuleFor(x => x.Confidence).InclusiveBetween(0, 1);
            d.RuleFor(x => x.Coverage).InclusiveBetween(0, 1);
        });

        RuleForEach(b => b.MetaDimensions).ChildRules(m =>
        {
            m.RuleFor(x => x.Id).NotEmpty();
            m.RuleFor(x => x.Lens).NotEmpty();
            m.RuleFor(x => x.ScoreZeroToTen!.Value).InclusiveBetween(0, 10).When(x => x.ScoreZeroToTen.HasValue);
        });
    }
}
