// Runtime checks for ContentValidation: do the catalog rules accept what the game accepts
// and reject what the game would choke on, before anything reaches the game?

using System;
using ScamWYF.Modding.Core.Content;

internal static class ContentTests
{
    private static int _passed;
    private static int _failed;

    private static void Check(string name, bool condition, string detail)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine("  PASS  " + name);
            return;
        }

        _failed++;
        Console.WriteLine("  FAIL  " + name + (detail.Length > 0 ? " - " + detail : ""));
    }

    private static ConversationScamSpec ValidScam()
    {
        return new ConversationScamSpec
        {
            Id = "moar-jury-duty",
            ProductId = "moar-scam-jury",
            AppId = "scam-moar-jury",
            Title = "Jury Duty",
            Reward = 475,
            Objectives = new[]
            {
                new ScamObjectiveSpec { Id = "summons", Text = "Get them to acknowledge the summons.", Prerequisites = new string[0] },
                new ScamObjectiveSpec { Id = "defer", Text = "Get them to ask about deferring.", Prerequisites = new[] { "summons" } },
                new ScamObjectiveSpec { Id = "confirm", Text = "Confirm the date.", Final = true, Prerequisites = new[] { "summons", "defer" } },
            },
        };
    }

    private static ScamTypeSpec ValidScamType()
    {
        return new ScamTypeSpec
        {
            Id = "moar_tax",
            DisplayName = "Tax Rebate",
            IconName = "bank",
            AppId = "moar-tax-office",
            DisplayOrder = 20,
            RelevantSentinels = new[] { "FICTIONAL_MOAR_TAX_ID" },
            AppCompletionSource = "moar_tax_submit",
            CallerPrompt = "Share your tax id [[FICTIONAL_MOAR_TAX_ID]] when persuaded.",
            TestOpeningPlayerLine = "I am calling about your tax rebate.",
        };
    }

    /// <summary>A game app with one field changed, for the rejection cases.</summary>
    private static OsAppSpec WithGame(Action<OsAppSpec> change)
    {
        var spec = OsAppSpec.GameApp("moar-slots", "Slots");
        change(spec);
        return spec;
    }

    private static ConversationScamPlan ValidPlan()
    {
        var scam = ValidScam();
        return ConversationScamPlan.New(scam.AppId, scam.ProductId, scam.Id, scam.Title,
            scam.Reward, scam.Objectives, 3);
    }

    private static FormScamPlan ValidFormPlan()
    {
        var sentinel = SentinelSpec.Digits("FICTIONAL_MOAR_TAX_ID", "Tax ID", 250,
            "moar_tax_submit", new[] { 2, 2 }, "-");
        return FormScamPlan.New("moar-tax-office", "moar-scam-tax", "moar_tax", "Tax Rebate",
            "moar_tax_submit", sentinel,
            new[]
            {
                new OsFormFieldSpec
                {
                    Id = "tax-id", Label = "Tax ID", ItemId = "FICTIONAL_MOAR_TAX_ID", Required = true
                }
            }, 3);
    }

    public static int Main()
    {
        // Conversation scams -------------------------------------------------
        Check("valid conversation scam passes",
            ContentValidation.CheckConversationScam(ValidScam()) == null,
            ContentValidation.CheckConversationScam(ValidScam()) ?? "");

        var zeroReward = ValidScam();
        zeroReward.Reward = 0;
        Check("reward 0 is rejected",
            ContentValidation.CheckConversationScam(zeroReward) != null, "accepted");

        var bigReward = ValidScam();
        bigReward.Reward = 10001;
        Check("reward over 10000 is rejected",
            ContentValidation.CheckConversationScam(bigReward) != null, "accepted");

        var oneObjective = ValidScam();
        oneObjective.Objectives = new[]
        {
            new ScamObjectiveSpec { Id = "only", Text = "Only.", Final = true, Prerequisites = new string[0] },
        };
        Check("a single objective is rejected",
            ContentValidation.CheckConversationScam(oneObjective) != null, "accepted");

        var noFinal = ValidScam();
        noFinal.Objectives = new[]
        {
            new ScamObjectiveSpec { Id = "a", Text = "A.", Prerequisites = new string[0] },
            new ScamObjectiveSpec { Id = "b", Text = "B.", Prerequisites = new string[0] },
        };
        Check("no final objective is rejected",
            ContentValidation.CheckConversationScam(noFinal) != null, "accepted");

        var optionalFinal = ValidScam();
        optionalFinal.Objectives[2].Optional = true;
        Check("an optional final is rejected",
            ContentValidation.CheckConversationScam(optionalFinal) != null, "accepted");

        var unknownPrereq = ValidScam();
        unknownPrereq.Objectives[1].Prerequisites = new[] { "nope" };
        Check("an unknown prerequisite is rejected",
            ContentValidation.CheckConversationScam(unknownPrereq) != null, "accepted");

        var selfPrereq = ValidScam();
        selfPrereq.Objectives[1].Prerequisites = new[] { "defer" };
        Check("a self prerequisite is rejected",
            ContentValidation.CheckConversationScam(selfPrereq) != null, "accepted");

        var cyclic = ValidScam();
        cyclic.Objectives[0].Prerequisites = new[] { "confirm" };
        Check("cyclic objectives are rejected",
            ContentValidation.CheckConversationScam(cyclic) != null, "accepted");

        var missingMandatory = ValidScam();
        missingMandatory.Objectives[2].Prerequisites = new[] { "summons" };
        Check("a final missing a mandatory milestone is rejected",
            ContentValidation.CheckConversationScam(missingMandatory) != null, "accepted");

        var badSubmission = ValidScam();
        badSubmission.Objectives[2].RequiresSubmission = true;
        Check("submission without a sentinel is rejected",
            ContentValidation.CheckConversationScam(badSubmission) != null, "accepted");

        var tiersMissingBase = ValidScam();
        tiersMissingBase.RewardTiers = new[] { 500, 600 };
        Check("tiers without the base reward are rejected",
            ContentValidation.CheckConversationScam(tiersMissingBase) != null, "accepted");

        var tiersOk = ValidScam();
        tiersOk.RewardTiers = new[] { 475, 600 };
        Check("tiers containing the base reward pass",
            ContentValidation.CheckConversationScam(tiersOk) == null,
            ContentValidation.CheckConversationScam(tiersOk) ?? "");

        // Scam types ----------------------------------------------------------
        Check("valid scam type passes",
            ContentValidation.CheckScamType(ValidScamType()) == null,
            ContentValidation.CheckScamType(ValidScamType()) ?? "");

        var noPrompt = ValidScamType();
        noPrompt.CallerPrompt = "";
        Check("a scam type without a caller prompt is rejected",
            ContentValidation.CheckScamType(noPrompt) != null, "accepted");

        var unmapped = ValidScamType();
        unmapped.RelevantSentinels = new[] { "FICTIONAL_MOAR_TAX_ID", "FICTIONAL_OTHER" };
        Check("a listed sentinel missing from the prompt is rejected",
            ContentValidation.CheckScamType(unmapped) != null, "accepted");

        var noSentinels = ValidScamType();
        noSentinels.RelevantSentinels = new string[0];
        Check("a scam type without sentinels is rejected",
            ContentValidation.CheckScamType(noSentinels) != null, "accepted");

        // Apps, products, sentinels -------------------------------------------
        var app = OsAppSpec.ConversationScamApp("scam-moar-jury", "Jury Duty", "moar-scam-jury");
        Check("a factory conversation app passes",
            ContentValidation.CheckApp(app) == null,
            ContentValidation.CheckApp(app) ?? "");

        var catalogNoProduct = OsAppSpec.ConversationScamApp("x", "X", "");
        Check("catalog access without a product is rejected",
            ContentValidation.CheckApp(catalogNoProduct) != null, "accepted");

        var formApp = OsAppSpec.FormScamApp("moar-tax-office", "Tax Office", "moar-scam-tax",
            "moar_tax_submit", "Verify",
            new[] { new OsFormFieldSpec { Id = "tax-id", Label = "Tax ID", ItemId = "FICTIONAL_MOAR_TAX_ID" } });
        Check("a factory form app passes",
            ContentValidation.CheckApp(formApp) == null,
            ContentValidation.CheckApp(formApp) ?? "");

        var product = StoreProductSpec.FreeScam("moar-scam-jury", "scam-moar-jury",
            "moar-jury-duty", "Jury Duty", "Serve justice.", 3);
        Check("a free scam product passes",
            ContentValidation.CheckProduct(product) == null,
            ContentValidation.CheckProduct(product) ?? "");

        var negativePrice = StoreProductSpec.FreeScam("p", "a", null, "T", "D", 1);
        negativePrice.Price = -5;
        Check("a negative price is rejected",
            ContentValidation.CheckProduct(negativePrice) != null, "accepted");

        var sentinel = SentinelSpec.Digits("FICTIONAL_MOAR_TAX_ID", "Tax ID", 250,
            "moar_tax_submit", new[] { 2, 2 }, "-");
        Check("a well-formed sentinel passes",
            ContentValidation.CheckSentinel(sentinel) == null,
            ContentValidation.CheckSentinel(sentinel) ?? "");

        var badTag = SentinelSpec.Digits("TAX_ID", "Tax ID", 250, "moar_tax_submit", null, null);
        Check("a tag without FICTIONAL_ is rejected",
            ContentValidation.CheckSentinel(badTag) != null, "accepted");

        // Apps the mod draws --------------------------------------------------
        var game = OsAppSpec.GameApp("moar-slots", "Slots");
        Check("a factory game app passes",
            ContentValidation.CheckGameApp(game) == null,
            ContentValidation.CheckGameApp(game) ?? "");

        Check("a game app that is neither on the desktop nor pinned is rejected",
            ContentValidation.CheckGameApp(WithGame(delegate (OsAppSpec spec)
            {
                spec.ShowOnDesktop = false;
            })) != null, "accepted");

        Check("a game app with a game-built contentType is rejected",
            ContentValidation.CheckGameApp(WithGame(delegate (OsAppSpec spec)
            {
                spec.ContentType = "notes";
            })) != null, "accepted");

        Check("a game app with a negative desktopOrder is rejected",
            ContentValidation.CheckGameApp(WithGame(delegate (OsAppSpec spec)
            {
                spec.DesktopOrder = -1;
            })) != null, "accepted");

        var gameWithoutId = OsAppSpec.GameApp("", "Slots");
        Check("a game app without an id is rejected",
            ContentValidation.CheckGameApp(gameWithoutId) != null, "accepted");

        // Scam plans ----------------------------------------------------------
        Check("a factory conversation scam plan passes",
            ContentValidation.CheckConversationScamPlan(ValidPlan()) == null,
            ContentValidation.CheckConversationScamPlan(ValidPlan()) ?? "");

        var planWrongApp = ValidPlan();
        planWrongApp.Product.AppId = "some-other-app";
        Check("a plan whose product unlocks a different app is rejected",
            ContentValidation.CheckConversationScamPlan(planWrongApp) != null, "accepted");

        var planWrongProduct = ValidPlan();
        planWrongProduct.Scam.ProductId = "moar-scam-other";
        Check("a plan whose scam names a different product is rejected",
            ContentValidation.CheckConversationScamPlan(planWrongProduct) != null, "accepted");

        var planWrongContentType = ValidPlan();
        planWrongContentType.App.ContentType = "notes";
        Check("a plan whose app cannot show a conversation scam is rejected",
            ContentValidation.CheckConversationScamPlan(planWrongContentType) != null, "accepted");

        var planNoApp = ValidPlan();
        planNoApp.App = null;
        Check("a plan with no app is rejected",
            ContentValidation.CheckConversationScamPlan(planNoApp) != null, "accepted");

        var planDuplicateTag = ValidPlan();
        planDuplicateTag.Sentinels = new[]
        {
            SentinelSpec.Digits("FICTIONAL_MOAR_TAX_ID", "Tax ID", 250, "moar_tax_submit", null, null),
            SentinelSpec.Digits("FICTIONAL_MOAR_TAX_ID", "Tax ID again", 250, "moar_tax_submit", null, null),
        };
        Check("a plan registering one caller value twice is rejected",
            ContentValidation.CheckConversationScamPlan(planDuplicateTag) != null, "accepted");

        Check("a valid form scam plan passes",
            ContentValidation.CheckFormScamPlan(ValidFormPlan()) == null,
            ContentValidation.CheckFormScamPlan(ValidFormPlan()) ?? "");

        var formWrongSource = ValidFormPlan();
        formWrongSource.App.Content.Binding.CompletionSource = "moar_tax_other";
        Check("a form whose app submits to a different source is rejected",
            ContentValidation.CheckFormScamPlan(formWrongSource) != null, "accepted");

        var formWrongTag = ValidFormPlan();
        formWrongTag.Sentinel.Tag = "FICTIONAL_MOAR_OTHER";
        formWrongTag.ScamType.RelevantSentinels = new[] { "FICTIONAL_MOAR_TAX_ID" };
        Check("a form whose caller value is not listed by the scam type is rejected",
            ContentValidation.CheckFormScamPlan(formWrongTag) != null, "accepted");

        var formNoFields = ValidFormPlan();
        formNoFields.App.Content.Fields = new OsFormFieldSpec[0];
        Check("a form with no fields is rejected",
            ContentValidation.CheckFormScamPlan(formNoFields) != null, "accepted");

        var formWrongAppType = ValidFormPlan();
        formWrongAppType.App.ContentType = "notes";
        Check("a form whose app is not a form is rejected",
            ContentValidation.CheckFormScamPlan(formWrongAppType) != null, "accepted");

        Check("null specs are rejected, not thrown",
            ContentValidation.CheckConversationScam(null) != null &&
            ContentValidation.CheckScamType(null) != null &&
            ContentValidation.CheckApp(null) != null &&
            ContentValidation.CheckProduct(null) != null &&
            ContentValidation.CheckSentinel(null) != null &&
            ContentValidation.CheckGameApp(null) != null &&
            ContentValidation.CheckConversationScamPlan(null) != null &&
            ContentValidation.CheckFormScamPlan(null) != null, "accepted null");

        Console.WriteLine();
        Console.WriteLine(_passed + " passed, " + _failed + " failed.");
        return _failed == 0 ? 0 : 1;
    }
}
