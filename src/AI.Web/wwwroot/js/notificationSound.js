// A short, soft two-tone chime for new items in the notification bell.
let audioContext;

window.aiClientPlayNotificationDing = async () => {
    const AudioContextType = window.AudioContext || window.webkitAudioContext;
    if (!AudioContextType) return;

    try {
        audioContext ??= new AudioContextType();
        if (audioContext.state === 'suspended') await audioContext.resume();
        if (audioContext.state !== 'running') return;

        const start = audioContext.currentTime;
        const volume = audioContext.createGain();
        volume.gain.setValueAtTime(0.0001, start);
        volume.gain.exponentialRampToValueAtTime(0.075, start + 0.012);
        volume.gain.exponentialRampToValueAtTime(0.0001, start + 0.32);
        volume.connect(audioContext.destination);

        for (const [frequency, level] of [[880, 1], [1320, 0.35]]) {
            const tone = audioContext.createOscillator();
            const mix = audioContext.createGain();
            tone.type = 'sine';
            tone.frequency.value = frequency;
            mix.gain.value = level;
            tone.connect(mix);
            mix.connect(volume);
            tone.start(start);
            tone.stop(start + 0.33);
            tone.onended = () => { tone.disconnect(); mix.disconnect(); };
        }
        setTimeout(() => volume.disconnect(), 400);
    } catch {
        // Browsers can reject audio until the user interacts with the page.
    }
};
