using VoiceChanger.Core;

namespace VoiceChanger.App;

/// <summary>Temporary one-button window for testing the logic. The real UI comes later.</summary>
public sealed class MainForm : Form
{
    private readonly VoiceChangerService _service = new();
    private readonly Button _clownButton;
    private readonly Label _status;

    public MainForm()
    {
        Text = "Voice Changer (prototype)";
        ClientSize = new Size(320, 200);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        _clownButton = new Button
        {
            Text = "Clown: OFF",
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            Size = new Size(240, 90),
            Location = new Point(40, 30),
        };
        _clownButton.Click += (_, _) => Toggle();

        _status = new Label
        {
            AutoSize = false,
            Size = new Size(300, 50),
            Location = new Point(10, 135),
            TextAlign = ContentAlignment.MiddleCenter,
        };

        Controls.Add(_clownButton);
        Controls.Add(_status);

        _service.Failed += ex => BeginInvoke(() => { _service.TurnOff(); ShowOff($"Audio error: {ex.Message}"); });
        ShowOff(AudioDevices.IsVbCableInstalled() ? "Ready" : "VB-Cable not found: will preview through speakers");
        FormClosed += (_, _) => _service.Dispose();
    }

    private void Toggle()
    {
        try
        {
            if (_service.IsOn)
            {
                _service.TurnOff();
                ShowOff("Real mic restored");
            }
            else
            {
                _service.TurnOn(Voice.Clown);
                _clownButton.Text = "Clown: ON";
                _clownButton.BackColor = Color.LightGreen;
                _status.Text = _service.IsPreviewOnly
                    ? "Preview only (use headphones). Install VB-Cable to use in apps."
                    : "Every app now hears your clown voice";
            }
        }
        catch (Exception ex)
        {
            _service.TurnOff();
            ShowOff($"Error: {ex.Message}");
        }
    }

    private void ShowOff(string status)
    {
        _clownButton.Text = "Clown: OFF";
        _clownButton.BackColor = SystemColors.Control;
        _status.Text = status;
    }
}
