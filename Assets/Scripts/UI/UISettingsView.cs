using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

/// <summary>Home-only settings. No battle state, pause or time-scale changes.</summary>
public class UISettingsView : UIPanel
{
    public const string Key="Prefabs/UISettingsView";
    public Button closeButton;
    public Slider musicSlider, soundSlider;
    public Toggle vibrationToggle;
    public Image vibrationTrack;
    public Sprite vibrationOn, vibrationOff;
    public RectTransform vibrationHandle, designRoot;
    bool _dirty;
    float _saveAt;
    public override void OnCreate()
    {
        closeButton.onClick.AddListener(Close);
        musicSlider.onValueChanged.AddListener(SetMusic);
        soundSlider.onValueChanged.AddListener(SetSound);
        vibrationToggle.onValueChanged.AddListener(SetVibration);
    }
    public override void OnOpen(object param=null)
    {
        musicSlider.SetValueWithoutNotify(AudioMgr.Instance.BgmVolume);
        soundSlider.SetValueWithoutNotify(AudioMgr.Instance.SfxVolume);
        vibrationToggle.SetIsOnWithoutNotify(GamePreferences.VibrationEnabled);
        RefreshVibration(); ApplyLayout();
    }
    void SetMusic(float value){AudioMgr.Instance.SetBgmVolume(value);MarkDirty();}
    void SetSound(float value){AudioMgr.Instance.SetSfxVolume(value);MarkDirty();}
    void SetVibration(bool enabled)
    {
        GamePreferences.VibrationEnabled=enabled;RefreshVibration();MarkDirty();
        if(enabled) WeChatService.Vibrate();
    }
    void RefreshVibration()
    {
        bool enabled=vibrationToggle.isOn;
        vibrationTrack.sprite=enabled?vibrationOn:vibrationOff;
        vibrationHandle.anchoredPosition=new Vector2(enabled?46f:-46f,0f);
    }
    void MarkDirty(){_dirty=true;_saveAt=Time.unscaledTime+.3f;}
    void Flush(){GamePreferences.Save();_dirty=false;}
    void Close(){Flush();UIMgr.Instance.ClosePanel(Key).Forget();}
    public override void OnClose(){Flush();}
    void OnDisable(){if(_dirty)Flush();}
    void OnApplicationPause(bool paused){if(paused&&_dirty)Flush();}
    void OnApplicationQuit(){if(_dirty)Flush();}
    void LateUpdate(){ApplyLayout();if(_dirty&&Time.unscaledTime>=_saveAt)Flush();}
    void ApplyLayout()
    {
        if(designRoot==null||Screen.width<=0||Screen.height<=0)return;
        var rect=((RectTransform)transform).rect;
        if(rect.width<=0||rect.height<=0)return;
        Rect safe=Screen.safeArea;
        float top=Screen.height-safe.yMax,bottom=safe.yMin;
        var capsule=WeChatService.SystemInfo?.GetMenuButtonRect();
        if(capsule.HasValue)top=Mathf.Max(top,capsule.Value.yMax+8f);
        float left=safe.xMin/Screen.width*rect.width;
        float right=(Screen.width-safe.xMax)/Screen.width*rect.width;
        float topUi=top/Screen.height*rect.height,bottomUi=bottom/Screen.height*rect.height;
        float scale=Mathf.Min((rect.width-left-right)/750f,(rect.height-topUi-bottomUi)/1334f);
        designRoot.localScale=Vector3.one*Mathf.Max(.01f,scale);
        designRoot.anchoredPosition=new Vector2((left-right)*.5f,(bottomUi-topUi)*.5f);
    }
}
