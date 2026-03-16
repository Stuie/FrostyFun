// Minimal TMPro stubs only - Unity types come from NuGet packages
// Only stubbing TMPro because there's no NuGet package for it

// ReSharper disable All
#pragma warning disable

namespace TMPro
{
    public class TMP_Text : UnityEngine.UI.Graphic
    {
        public string text { get; set; }
    }

    public class TextMeshProUGUI : TMP_Text { }

    public class TMP_InputField : UnityEngine.UI.Selectable
    {
        public string text { get; set; }
        public SubmitEvent onSelect => null;
        public SubmitEvent onSubmit => null;
        public void Select() { }
        public void ActivateInputField() { }

        public class SubmitEvent : UnityEngine.Events.UnityEvent<string> { }
    }
}

// Il2Cpp namespace alias used by MelonLoader
namespace Il2CppTMPro
{
    public class TMP_Text : UnityEngine.UI.Graphic
    {
        public string text { get; set; }
    }

    public class TextMeshProUGUI : TMP_Text { }

    public class TMP_InputField : UnityEngine.UI.Selectable
    {
        public string text { get; set; }
        public SubmitEvent onSelect => null;
        public SubmitEvent onSubmit => null;
        public void Select() { }
        public void ActivateInputField() { }

        public class SubmitEvent : UnityEngine.Events.UnityEvent<string> { }
    }
}
