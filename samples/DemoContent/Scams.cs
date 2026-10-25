using ScamWYF.Modding.Core.Content;

namespace ScamWYF.DemoContent
{
    /// <summary>
    /// The demo's two scams, each built as one plan so the ids, the completion sources and the caller
    /// values that have to agree are written once.
    /// </summary>
    /// <remarks>
    /// A plan is plain data: <c>New</c> fills in a working app, product and scam from the same ids, and
    /// any field can be rewritten afterwards, which is how the prompts below are replaced with words
    /// that suit the scam rather than the generated placeholder.
    /// </remarks>
    internal static class Scams
    {
        /// <summary>The caller value the dog walking scam's last step verifies against.</summary>
        internal const string DogTag = "FICTIONAL_DEMO_DOG_TAG";

        /// <summary>The completion source the dog walking app submits to.</summary>
        internal const string DogSubmit = "demo_dog_submit";

        /// <summary>The caller value the library card form verifies.</summary>
        internal const string LibraryCardTag = "FICTIONAL_DEMO_LIBRARY_CARD";

        /// <summary>The completion source the library form submits to.</summary>
        internal const string LibrarySubmit = "demo_library_submit";

        /// <summary>
        /// A conversation scam: objectives that check off as the player talks, ending in a value the
        /// caller has to read out - the shape the game's own romance and tech-support scams use.
        /// </summary>
        internal static ConversationScamPlan DogWalking()
        {
            var plan = ConversationScamPlan.New("demo-dog-walking", "demo-product-dog", "demo-dog-scam",
                "Dog Walking Scam", 300, new[]
                {
                    new ScamObjectiveSpec
                    {
                        Id = "walk",
                        Text = "Get the caller to agree to a walk.",
                        Criteria = "Judge (not the caller): if the caller agrees to a walk.",
                        Prerequisites = new string[0],
                    },
                    new ScamObjectiveSpec
                    {
                        Id = "treat",
                        Text = "Get the caller to promise a treat.",
                        Criteria = "Judge (not the caller): if the caller promises a treat.",
                        Prerequisites = new[] { "walk" },
                    },
                    new ScamObjectiveSpec
                    {
                        Id = "tag",
                        Text = "Get the caller to read out the dog's tag number.",
                        Criteria = "Judge (not the caller): if the caller reads the tag number out loud.",
                        RequiredSentinel = DogTag,
                        Prerequisites = new[] { "walk", "treat" },
                    },
                    new ScamObjectiveSpec
                    {
                        Id = "pay",
                        Text = "Enter the tag number the caller gave you.",
                        Criteria = "Judge (not the caller): if the caller pays for the walking package.",
                        RequiredSentinel = DogTag,
                        RequiresSubmission = true,
                        Final = true,
                        Prerequisites = new[] { "walk", "treat", "tag" },
                    },
                }, 1,
                "Sell a walk, a treat and the tag number that proves it.", "KolkataOS/SVG/notebook",
                "#2E7D32", 96);

            // The scam's own tag. The game refuses a scam whose objective names a different one, and
            // refuses two scams claiming the same product or app.
            plan.Scam.Sentinel = DogTag;
            plan.Sentinels = new[]
            {
                SentinelSpec.Digits(DogTag, "Dog tag number", 120, DogSubmit, new[] { 3, 3 }, "-"),
            };

            plan.Scam.CallerPrompt = "You are a lonely dog owner who wants someone to walk the dog.";
            return plan;
        }

        /// <summary>
        /// A form scam: an app with fields, a caller value, and a scam type telling the caller script
        /// which value matters. The bank-login shape, from a single completion source.
        /// </summary>
        internal static FormScamPlan LibraryCard()
        {
            var plan = FormScamPlan.New("demo-library-card", "demo-product-library", "demo-library-scam",
                "Library Card", LibrarySubmit,
                SentinelSpec.Code(LibraryCardTag, "Library card number", 150, LibrarySubmit,
                    new[] { 2, 2 }, "-"),
                new[]
                {
                    new OsFormFieldSpec
                    {
                        Id = "card",
                        Label = "Card number",
                        Placeholder = "XX-XX",
                        ItemId = LibraryCardTag,
                        Required = true,
                        PersistLocal = true,
                    },
                    new OsFormFieldSpec
                    {
                        Id = "pin",
                        Label = "PIN",
                        Placeholder = "4 digits",
                        Required = false,
                        Password = true,
                    },
                }, 1,
                "Pay the overdue fee with the card number they keep by the phone.", "KolkataOS/SVG/notebook",
                "#3B5BA5", 97);

            // Written out rather than generated: the caller has to say the tag the form verifies, and
            // the prompt is the only place that tells it to.
            plan.ScamType.CallerPrompt =
                "Read the library card number back as [[" + LibraryCardTag + "]] when the player asks for it.";
            plan.ScamType.TestOpeningPlayerLine =
                "Hi, this is the city library about your overdue card.";
            plan.ScamType.DisplayOrder = 110;
            return plan;
        }
    }
}
