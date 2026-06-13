using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    // ── SillyTavern V2 character card JSON types ──────────────────────────────

    [DataContract] internal class StCharaV2
    {
        [DataMember(Name = "spec")]         public string Spec        { get; set; } = "chara_card_v2";
        [DataMember(Name = "spec_version")] public string SpecVersion { get; set; } = "2.0";
        [DataMember(Name = "data")]         public StCharaData Data   { get; set; }
    }

    [DataContract] internal class StCharaData
    {
        [DataMember(Name = "name")]                      public string Name        { get; set; } = "";
        [DataMember(Name = "description")]               public string Description { get; set; } = "";
        [DataMember(Name = "personality")]               public string Personality { get; set; } = "";
        [DataMember(Name = "scenario")]                  public string Scenario    { get; set; } = "";
        [DataMember(Name = "first_mes")]                 public string FirstMes    { get; set; } = "";
        [DataMember(Name = "mes_example")]               public string MesExample  { get; set; } = "";
        [DataMember(Name = "creator_notes")]             public string CreatorNotes { get; set; } = "";
        [DataMember(Name = "system_prompt")]             public string SystemPrompt { get; set; } = "";
        [DataMember(Name = "post_history_instructions")] public string PostHistory  { get; set; } = "";
        [DataMember(Name = "tags")]                      public List<string> Tags   { get; set; } = new List<string>();
        [DataMember(Name = "creator")]                   public string Creator      { get; set; } = "";
        [DataMember(Name = "character_version")]         public string Version      { get; set; } = "";
    }

    // ── SillyTavern world book JSON types ─────────────────────────────────────

    [DataContract] internal class StWorldBook
    {
        [DataMember(Name = "name")]    public string Name    { get; set; } = "世界书";
        [DataMember(Name = "entries")] public Dictionary<string, StWorldEntry> Entries { get; set; }
            = new Dictionary<string, StWorldEntry>();
    }

    [DataContract] internal class StWorldEntry
    {
        [DataMember(Name = "uid")]      public int    Uid      { get; set; }
        [DataMember(Name = "key")]      public List<string> Key { get; set; } = new List<string>();
        [DataMember(Name = "comment")]  public string Comment  { get; set; } = "";
        [DataMember(Name = "content")]  public string Content  { get; set; } = "";
        [DataMember(Name = "constant")] public bool   Constant { get; set; }
        [DataMember(Name = "selective")]public bool   Selective{ get; set; }
        [DataMember(Name = "order")]    public int    Order    { get; set; } = 100;
        [DataMember(Name = "disable")]  public bool   Disable  { get; set; }
    }

    // ── ST chat JSONL types ───────────────────────────────────────────────────

    [DataContract] internal class StChatMeta
    {
        [DataMember(Name = "user_name")]      public string UserName      { get; set; } = "用户";
        [DataMember(Name = "character_name")] public string CharacterName { get; set; } = "AI";
        [DataMember(Name = "create_date")]    public string CreateDate    { get; set; } = "";
        [DataMember(Name = "chat_metadata")]  public object ChatMetadata  { get; set; } = new object();
    }

    [DataContract] internal class StChatMessage
    {
        [DataMember(Name = "name")]      public string Name     { get; set; }
        [DataMember(Name = "is_user")]   public bool   IsUser   { get; set; }
        [DataMember(Name = "send_date")] public string SendDate { get; set; } = "";
        [DataMember(Name = "mes")]       public string Mes      { get; set; } = "";
        [DataMember(Name = "swipes")]    public List<string> Swipes  { get; set; }
        [DataMember(Name = "swipe_id")]  public int   SwipeId  { get; set; }

        public string ResolvedContent
        {
            get
            {
                if (Swipes != null && Swipes.Count > 0)
                {
                    int idx = SwipeId >= 0 && SwipeId < Swipes.Count ? SwipeId : 0;
                    var sw = Swipes[idx];
                    if (!string.IsNullOrEmpty(sw)) return sw;
                }
                return Mes ?? "";
            }
        }
    }

    // ── Yanshu API profile export types ──────────────────────────────────────

    [DataContract] internal class YanshuApiExport
    {
        [DataMember(Name = "format")]   public string Format   { get; set; } = "yanshu_api_profiles";
        [DataMember(Name = "version")]  public string Version  { get; set; } = "1.0";
        [DataMember(Name = "profiles")] public List<YanshuApiEntry> Profiles { get; set; }
            = new List<YanshuApiEntry>();
    }

    [DataContract] internal class YanshuApiEntry
    {
        [DataMember(Name = "name")]    public string Name   { get; set; }
        [DataMember(Name = "url")]     public string Url    { get; set; }
        [DataMember(Name = "api_key")] public string ApiKey { get; set; }
        [DataMember(Name = "model")]   public string Model  { get; set; }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Page
    // ═════════════════════════════════════════════════════════════════════════

    public sealed partial class ImportExportPage : Page
    {
        public ImportExportPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
        }

    }
}
