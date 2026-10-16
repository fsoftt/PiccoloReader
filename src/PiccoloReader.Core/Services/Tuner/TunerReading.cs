namespace PiccoloReader.Core.Services.Tuner;

/// <summary>Smoothed tuner output. CentsFromA4 is relative to the fixed 440 Hz reference, independent of the A4 setting.</summary>
public readonly record struct TunerReading(bool HasPitch, float Hz, float CentsFromA4);
