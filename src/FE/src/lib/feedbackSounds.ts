let audioContext: AudioContext | null = null;

const getAudioContext = () => {
  if (typeof window === 'undefined') return null;
  const AudioContextCtor = window.AudioContext ?? (window as typeof window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
  if (!AudioContextCtor) return null;

  audioContext ??= new AudioContextCtor();
  return audioContext;
};

const playTone = (context: AudioContext, frequency: number, startAt: number, duration: number, gainValue: number) => {
  const oscillator = context.createOscillator();
  const gain = context.createGain();

  oscillator.type = 'sine';
  oscillator.frequency.setValueAtTime(frequency, startAt);
  gain.gain.setValueAtTime(0.0001, startAt);
  gain.gain.exponentialRampToValueAtTime(gainValue, startAt + 0.015);
  gain.gain.exponentialRampToValueAtTime(0.0001, startAt + duration);

  oscillator.connect(gain);
  gain.connect(context.destination);
  oscillator.start(startAt);
  oscillator.stop(startAt + duration + 0.02);
};

const playSequence = async (notes: Array<{ frequency: number; offset: number; duration: number; gain: number }>) => {
  const context = getAudioContext();
  if (!context) return;

  try {
    if (context.state === 'suspended') await context.resume();
    const now = context.currentTime + 0.01;
    notes.forEach((note) => playTone(context, note.frequency, now + note.offset, note.duration, note.gain));
  } catch {
    // Browsers may block audio until the first explicit user gesture. Visual feedback remains available.
  }
};

export const primeFeedbackAudio = async () => {
  const context = getAudioContext();
  if (!context) return;

  try {
    if (context.state === 'suspended') await context.resume();
  } catch {
    // Best-effort browser audio unlock only.
  }
};

export const playStartupJingle = () => playSequence([
  { frequency: 523.25, offset: 0, duration: 0.13, gain: 0.035 },
  { frequency: 659.25, offset: 0.10, duration: 0.15, gain: 0.04 },
  { frequency: 783.99, offset: 0.20, duration: 0.20, gain: 0.045 },
]);

export const playCompletionJingle = () => playSequence([
  { frequency: 659.25, offset: 0, duration: 0.14, gain: 0.045 },
  { frequency: 783.99, offset: 0.11, duration: 0.16, gain: 0.05 },
  { frequency: 1046.50, offset: 0.23, duration: 0.28, gain: 0.055 },
]);
