namespace CodexVtt.Core.Audio;

/// <summary>
/// Conversione a mono e ricampionamento a 16 kHz con interpolazione lineare e filtro passa-basso
/// a media mobile quando si riduce la frequenza. È adeguato al parlato; per i formati compressi su
/// Windows il ricampionamento lo esegue Media Foundation.
/// </summary>
public sealed class StreamingResampler
{
    private readonly int _channels;
    private readonly double _step;
    private readonly int _lowPassWidth;
    private readonly Queue<float> _window = new();
    private double _windowSum;
    private double _position;
    private float _previous;
    private bool _hasPrevious;

    public StreamingResampler(int sourceRate, int channels, int targetRate = AudioConstants.WhisperSampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sourceRate, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(channels, 0);
        _channels = channels;
        _step = (double)sourceRate / targetRate;
        _lowPassWidth = Math.Max(1, (int)Math.Round(_step));
    }

    /// <summary>Elabora campioni interleaved e aggiunge l'output mono ricampionato a <paramref name="output"/>.</summary>
    public void Process(ReadOnlySpan<float> interleaved, List<float> output)
    {
        for (var frame = 0; frame + _channels <= interleaved.Length; frame += _channels)
        {
            float mono = 0;
            for (var c = 0; c < _channels; c++)
                mono += interleaved[frame + c];
            mono /= _channels;

            var filtered = LowPass(mono);
            if (!_hasPrevious)
            {
                _previous = filtered;
                _hasPrevious = true;
                output.Add(filtered);
                _position += _step;
                continue;
            }

            // Il campione sorgente corrente copre l'intervallo (t-1, t]; emettiamo gli output che vi cadono.
            while (_position <= 1.0)
            {
                output.Add(_previous + (float)((filtered - _previous) * _position));
                _position += _step;
            }

            _position -= 1.0;
            _previous = filtered;
        }
    }

    private float LowPass(float sample)
    {
        if (_lowPassWidth == 1)
            return sample;
        _window.Enqueue(sample);
        _windowSum += sample;
        if (_window.Count > _lowPassWidth)
            _windowSum -= _window.Dequeue();
        return (float)(_windowSum / _window.Count);
    }
}
