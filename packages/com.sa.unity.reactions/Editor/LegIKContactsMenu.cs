using UnityEditor;

namespace SA.Reactions.Editor
{
    static class LegIKContactsMenu
    {
        const string Path = "Tools/SA/Show Leg IK Contacts";

        [MenuItem(Path)]
        static void Toggle() => EditorPrefs.SetBool(LegIKWatchSystem.ContactsKey, !EditorPrefs.GetBool(LegIKWatchSystem.ContactsKey, true));

        [MenuItem(Path, true)]
        static bool Check()
        {
            Menu.SetChecked(Path, EditorPrefs.GetBool(LegIKWatchSystem.ContactsKey, true));
            return true;
        }
    }
}
