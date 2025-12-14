using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

public class AlertToggle : MonoBehaviour
{
    public GameObject alertPanel;

    void Update()
    {
        if (PressedSpaceThisFrame())
            TogglePanel();
    }

    bool PressedSpaceThisFrame()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }

    void TogglePanel()
    {
        if (!alertPanel) return;
        alertPanel.SetActive(!alertPanel.activeSelf);
    }

    // 给关闭按钮用
    public void HideAlert()
    {
        if (alertPanel) alertPanel.SetActive(false);
    }
}
