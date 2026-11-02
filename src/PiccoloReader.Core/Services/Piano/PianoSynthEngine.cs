using System.Collections.Concurrent;
using MeltySynth;

namespace PiccoloReader.Core.Services.Piano;

/// <summary>
/// MeltySynth wrapper that plays one instrument on channel 0. Any thread enqueues commands;
/// the audio thread applies them at the start of each <see cref="Render"/> block, so the
/// synthesizer is only ever touched by one thread and the hot path takes no lock. Rendering
/// allocates nothing.
/// </summary>
public sealed class PianoSynthEngine
{
    /// <summary>Keeps the loudest preset at about -3 dBFS for a single note.</summary>
    public const float MasterGain = 1.69f;

    // Samples below the knee pass untouched; above it a tanh curve approaches 1 so chords never clip.
    private const float LimiterKnee = 0.75f;
    private const int Channel = 0;
    private const int ProgramChange = 0xC0;
    private const int ControlChange = 0xB0;
    private const int SustainController = 64;

    private enum CommandKind : byte
    {
        NoteOn,
        NoteOff,
        Sustain,
        Instrument,
        Silence
    }

    private readonly record struct Command(CommandKind Kind, int A, int B, float Gain);

    private readonly Synthesizer _synthesizer;
    private readonly ConcurrentQueue<Command> _commands = new();
    private float _gain;
    private float _targetGain;

    public PianoSynthEngine(SoundFont soundFont, int sampleRate, PianoInstrument? instrument = null)
    {
        var settings = new SynthesizerSettings(sampleRate);
        _synthesizer = new Synthesizer(soundFont, settings);
        SampleRate = sampleRate;
        var initial = instrument ?? PianoInstruments.Default;
        Apply(new Command(CommandKind.Instrument, initial.GmProgram, 0, initial.Gain));
        _gain = _targetGain;
    }

    public int SampleRate { get; }

    /// <summary>Voices sounding right now. Only meaningful on the audio thread (or offline).</summary>
    public int ActiveVoiceCount => _synthesizer.ActiveVoiceCount;

    public void NoteOn(int note, int velocity) =>
        _commands.Enqueue(new Command(CommandKind.NoteOn, note, Math.Clamp(velocity, 1, 127), 0f));

    public void NoteOff(int note) =>
        _commands.Enqueue(new Command(CommandKind.NoteOff, note, 0, 0f));

    public void SetSustain(bool on) =>
        _commands.Enqueue(new Command(CommandKind.Sustain, on ? 127 : 0, 0, 0f));

    /// <summary>Releases the sounding notes (they ring out) and switches to the new sound.</summary>
    public void SetInstrument(PianoInstrument instrument) =>
        _commands.Enqueue(new Command(CommandKind.Instrument, instrument.GmProgram, 0, instrument.Gain));

    /// <summary>Stops every voice at once and lifts the pedal. Queued before the notes that follow it.</summary>
    public void Silence() => _commands.Enqueue(new Command(CommandKind.Silence, 0, 0, 0f));

    /// <summary>Fills both buffers (same length) with the next block of stereo audio. Audio thread only.</summary>
    public void Render(Span<float> left, Span<float> right)
    {
        while (_commands.TryDequeue(out var command))
        {
            Apply(command);
        }

        var count = Math.Min(left.Length, right.Length);
        left = left[..count];
        right = right[..count];
        _synthesizer.Render(left, right);

        // The gain eases to its target over the block, so an instrument switch never clicks.
        var gain = _gain;
        var step = count == 0 ? 0f : (_targetGain - gain) / count;
        for (var i = 0; i < count; i++)
        {
            gain += step;
            left[i] = Limit(left[i] * gain);
            right[i] = Limit(right[i] * gain);
        }

        _gain = _targetGain;
    }

    private void Apply(Command command)
    {
        switch (command.Kind)
        {
            case CommandKind.NoteOn:
                _synthesizer.NoteOn(Channel, command.A, command.B);
                break;
            case CommandKind.NoteOff:
                _synthesizer.NoteOff(Channel, command.A);
                break;
            case CommandKind.Sustain:
                _synthesizer.ProcessMidiMessage(Channel, ControlChange, SustainController, command.A);
                break;
            case CommandKind.Instrument:
                _synthesizer.NoteOffAll(Channel, false);
                _synthesizer.ProcessMidiMessage(Channel, ProgramChange, command.A, 0);
                _targetGain = command.Gain * MasterGain;
                break;
            case CommandKind.Silence:
                _synthesizer.NoteOffAll(Channel, true);
                _synthesizer.ProcessMidiMessage(Channel, ControlChange, SustainController, 0);
                break;
        }
    }

    private static float Limit(float x)
    {
        if (float.IsNaN(x))
        {
            return 0f;
        }

        var magnitude = MathF.Abs(x);
        if (magnitude <= LimiterKnee)
        {
            return x;
        }

        var limited = LimiterKnee + ((1f - LimiterKnee) * MathF.Tanh((magnitude - LimiterKnee) / (1f - LimiterKnee)));
        return MathF.CopySign(MathF.Min(limited, 1f), x);
    }
}
