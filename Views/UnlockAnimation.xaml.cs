using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using SoleKey.ViewModels;

namespace SoleKey.Views;

public partial class UnlockAnimation : UserControl
{
    private Storyboard? running;
    private TaskCompletionSource? opening;
    public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(nameof(Phase), typeof(UnlockPhase), typeof(UnlockAnimation),
        new PropertyMetadata(UnlockPhase.Idle, (d, _) => ((UnlockAnimation)d).UpdatePhase()));
    public UnlockPhase Phase { get => (UnlockPhase)GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }
    public UnlockAnimation()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdatePhase();
        Unloaded += (_, _) => { Stop(); opening?.TrySetResult(); opening = null; };
    }
    private void Stop()
    {
        var position = KeyMove.X;
        running?.Remove(this); running = null;
        KeyMove.X = position;
    }
    private void Reset()
    {
        Stop(); opening?.TrySetResult(); opening = null;
        KeyMove.X = 0; KeyTurn.ScaleY = 1; KeyTilt.Angle = 0; Shake.X = 0;
        DoorSwing.ScaleX = 1; DoorSkew.AngleY = 0; KeyShape.Opacity = 1;
        OpenMark.Opacity = 0; VaultHalo.Opacity = 0.2; KeyGlow.Opacity = 0;
        MarkScale.ScaleX = MarkScale.ScaleY = 1;
    }
    private void Play(string resource)
    {
        running = ((Storyboard)Resources[resource]).Clone();
        running.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
    }
    private void UpdatePhase()
    {
        if (Caption is null || !IsLoaded) return;
        Caption.SetResourceReference(TextBlock.ForegroundProperty, Phase == UnlockPhase.Failed ? "DangerBrush" : "MutedBrush");
        switch (Phase)
        {
            case UnlockPhase.Idle:
                Reset(); Caption.Text = "마스터 비밀번호로 여는 나만의 금고"; break;
            case UnlockPhase.Checking:
                Reset(); Caption.Text = "열쇠를 맞추는 중…";
                if (SystemParameters.ClientAreaAnimation) Play("CheckingMotion"); else KeyMove.X = 88;
                break;
            case UnlockPhase.Opening:
                _ = PlayOpeningAsync(); break;
            case UnlockPhase.Failed:
                Stop(); Caption.Text = "금고를 열지 못했습니다.";
                if (SystemParameters.ClientAreaAnimation) Play("FailedMotion"); else KeyMove.X = 0;
                break;
        }
    }
    public Task PlayOpeningAsync()
    {
        if (opening is not null) return opening.Task;
        Stop(); opening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Caption.Text = "열쇠를 돌리는 중…";
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation)
        {
            DoorSwing.ScaleX = 0.13; KeyShape.Opacity = 0; OpenMark.Opacity = 1;
            Caption.Text = "금고가 열렸습니다.";
            opening.TrySetResult(); return opening.Task;
        }
        running = ((Storyboard)Resources["OpeningMotion"]).Clone();
        var completion = opening;
        running.Completed += (_, _) => completion.TrySetResult();
        running.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
        return completion.Task;
    }
}
