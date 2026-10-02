// A soft "ratchet tooth" tick played when the cursor crosses from one history marker
// to the next while hovering the strip. Synthesized (no asset to ship) the same way the
// notification ding is: a shared AudioContext + a small graph that disconnects itself.
//
// Reuse one brief, enveloped click so every tooth has the same timbre. A band-limited
// transient and a faint, damped resonance give it body without a long noise tail.
// Keep the context local: notificationSound.js is also loaded as a classic script.
(() => {
    let audioContext;
    let tickBuffer;
    let lastTickAt = -Infinity;
    let resuming = false;

    const createTickBuffer = context => {
        const sampleRate = context.sampleRate;
        const length = Math.ceil(sampleRate * 0.024);
        const buffer = context.createBuffer(1, length, sampleRate);
        const data = buffer.getChannelData(0);
        const upperCutoff = 1 - Math.exp(-2 * Math.PI * 1800 / sampleRate);
        const lowerCutoff = 1 - Math.exp(-2 * Math.PI * 350 / sampleRate);
        let upper = 0;
        let lower = 0;
        for (let index = 0; index < length; index++) {
            const time = index / sampleRate;
            const noise = Math.random() * 2 - 1;
            upper += upperCutoff * (noise - upper);
            lower += lowerCutoff * (noise - lower);
            const attack = Math.min(1, time / 0.001);
            const fade = Math.min(1, (length - 1 - index) / (sampleRate * 0.004));
            const envelope = attack * Math.exp(-time / 0.004) * fade;
            const body = Math.sin(2 * Math.PI * 780 * time) * 0.15;
            data[index] = (upper - lower + body) * envelope * 0.09;
        }
        return buffer;
    };

    window.aiClientPlayRatchetTick = async () => {
        if (document.documentElement.dataset.otherSounds !== 'true') return;
        const AudioContextType = window.AudioContext || window.webkitAudioContext;
        if (!AudioContextType) return;

        try {
            audioContext ??= new AudioContextType();
            if (audioContext.state === 'suspended') {
                // Do not queue a burst of stale hover ticks while audio is being unlocked.
                if (resuming) return;
                resuming = true;
                try {
                    await audioContext.resume();
                } finally {
                    resuming = false;
                }
            }
            if (audioContext.state !== 'running') return;

            const start = audioContext.currentTime;
            if (start - lastTickAt < 0.028) return;
            tickBuffer ??= createTickBuffer(audioContext);
            const tick = audioContext.createBufferSource();
            tick.buffer = tickBuffer;
            tick.connect(audioContext.destination);
            tick.onended = () => tick.disconnect();
            tick.start(start);
            lastTickAt = start;
        } catch {
            // Browsers can reject audio until the user interacts with the page; the ratchet tick
            // is decoration, not feedback, so a silent miss is acceptable.
        }
    };
})();
