namespace yanshuai
{
    /// <summary>跨向导页面传递的临时数据，导航时不会丢失</summary>
    internal static class CharaWizardData
    {
        public static string Name             { get; set; } = "";
        public static string AvatarBase64     { get; set; }
        public static string AvatarMimeType   { get; set; }
        public static string IllustBase64     { get; set; }
        public static string IllustMimeType   { get; set; }
        public static string Description      { get; set; } = "";
        public static string Personality      { get; set; } = "";
        public static string Scenario         { get; set; } = "";
        public static string FirstMessage     { get; set; } = "";
        public static string SystemPrompt             { get; set; } = "";
        public static string PostHistoryInstructions  { get; set; } = "";
        public static string MesExample               { get; set; } = "";
        public static string CreatorNotes             { get; set; } = "";
        public static string Tags                     { get; set; } = "";
        public static string Creator                  { get; set; } = "";
        public static string CharacterVersion         { get; set; } = "";

        /// <summary>非空时表示编辑已有角色卡，完成时更新而非新建</summary>
        public static string EditingCardId    { get; set; } = null;

        /// <summary>向导结束时由哪个页面接收完成回调（OOBE or 角色卡页）</summary>
        public static string ReturnTarget     { get; set; } = "CharacterCardsPage";

        public static void Reset()
        {
            Name = ""; Description = ""; Personality = ""; Scenario = ""; FirstMessage = "";
            SystemPrompt = ""; PostHistoryInstructions = ""; MesExample = "";
            CreatorNotes = ""; Tags = ""; Creator = ""; CharacterVersion = "";
            AvatarBase64 = null; AvatarMimeType = null;
            IllustBase64 = null; IllustMimeType = null;
            EditingCardId = null;
            ReturnTarget = "CharacterCardsPage";
        }

        public static void LoadFromCard(CharacterCard card)
        {
            EditingCardId        = card.Id;
            Name                 = card.Name                    ?? "";
            Description          = card.Description             ?? "";
            Personality          = card.Personality             ?? "";
            Scenario             = card.Scenario                ?? "";
            FirstMessage         = card.FirstMessage            ?? "";
            SystemPrompt         = card.SystemPrompt            ?? "";
            PostHistoryInstructions = card.PostHistoryInstructions ?? "";
            MesExample           = card.MesExample              ?? "";
            CreatorNotes         = card.CreatorNotes            ?? "";
            Tags                 = card.Tags                    ?? "";
            Creator              = card.Creator                 ?? "";
            CharacterVersion     = card.CharacterVersion        ?? "";
            AvatarBase64         = card.AvatarBase64;
            AvatarMimeType       = card.AvatarMimeType;
            IllustBase64         = card.IllustrationBase64;
            IllustMimeType       = card.IllustrationMimeType;
        }

        public static void ApplyToCard(CharacterCard card)
        {
            card.Name                    = Name;
            card.Description             = Description;
            card.Personality             = Personality;
            card.Scenario                = Scenario;
            card.FirstMessage            = FirstMessage;
            card.SystemPrompt            = SystemPrompt;
            card.PostHistoryInstructions = PostHistoryInstructions;
            card.MesExample              = MesExample;
            card.CreatorNotes            = CreatorNotes;
            card.Tags                    = Tags;
            card.Creator                 = Creator;
            card.CharacterVersion        = CharacterVersion;
            card.AvatarBase64            = AvatarBase64;
            card.AvatarMimeType          = AvatarMimeType;
            card.IllustrationBase64      = IllustBase64;
            card.IllustrationMimeType    = IllustMimeType;
        }

        public static CharacterCard ToCard()
        {
            var card = new CharacterCard();
            ApplyToCard(card);
            return card;
        }
    }
}
