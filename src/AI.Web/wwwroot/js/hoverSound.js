// A soft "ratchet tooth" tick played when the cursor crosses from one history marker
// to the next while hovering the strip. Synthesized (no asset to ship) the same way the
// notification ding is: a shared AudioContext + a small graph that disconnects itself.
//
// Two layers give the click its mechanical-tooth flavour:
//   1. A short, low-passed noise burst for a soft "tk" without a sharp high end.
//   2. A short, low-passed noise burst around 220 Hz for the body — the wooden "thud".
// A pure noise burst alone reads as a "tick"; layering in low-end body is what makes it read
// as something physical hitting something else.
// Keep the context local: notificationSound.js is also loaded as a classic script.
(() => {
    let audioContext;

    window.aiClientPlayRatchetTick = async () => {
        if (document.documentElement.dataset.otherSounds !== 'true') return;
        const AudioContextType = window.AudioContext || window.webkitAudioContext;
        if (!AudioContextType) return;

        try {
            audioContext ??= new AudioContextType();
            if (audioContext.state === 'suspended') await audioContext.resume();
            if (audioContext.state !== 'running') return;

            const start = audioContext.currentTime;
            const sampleRate = audioContext.sampleRate;

            // A slower attack and short fade soften the leading edge while keeping fast hovers distinct.
            const tickLength = 0.065;
            const volume = audioContext.createGain();
            volume.gain.setValueAtTime(0.0001, start);
            volume.gain.exponentialRampToValueAtTime(0.12, start + 0.012);
            volume.gain.exponentialRampToValueAtTime(0.0001, start + tickLength);
            volume.connect(audioContext.destination);

            // 1. A longer, low-passed transient avoids the brittle noise spike.
            const transientLength = 0.025;
            const transientBufferLength = Math.max(1, Math.floor(sampleRate * transientLength));
            const transientBuffer = audioContext.createBuffer(1, transientBufferLength, sampleRate);
            const transientData = transientBuffer.getChannelData(0);
            for (let index = 0; index < transientBufferLength; index++) transientData[index] = Math.random() * 2 - 1;
            const transient = audioContext.createBufferSource();
            transient.buffer = transientBuffer;
            const transientFilter = audioContext.createBiquadFilter();
            transientFilter.type = 'lowpass';
            transientFilter.frequency.value = 1100;
            transientFilter.Q.value = 0.7;
            transient.connect(transientFilter);
            transientFilter.connect(volume);
            transient.start(start);
            transient.stop(start + transientLength);

            // 2. Body: full-length low-passed noise. Lowpass at 220 Hz with Q=0.7 keeps it from
            // sounding like a sine sweep; the click character comes from the noise source itself,
            // not from a sine.
            const bodyBufferLength = Math.max(1, Math.floor(sampleRate * tickLength));
            const bodyBuffer = audioContext.createBuffer(1, bodyBufferLength, sampleRate);
            const bodyData = bodyBuffer.getChannelData(0);
            for (let index = 0; index < bodyBufferLength; index++) bodyData[index] = Math.random() * 2 - 1;
            const body = audioContext.createBufferSource();
            body.buffer = bodyBuffer;
            const bodyFilter = audioContext.createBiquadFilter();
            bodyFilter.type = 'lowpass';
            bodyFilter.frequency.value = 220;
            bodyFilter.Q.value = 0.7;
            body.connect(bodyFilter);
            bodyFilter.connect(volume);
            body.start(start);
            body.stop(start + tickLength);

            // Disconnect AFTER both source stops — disconnecting earlier can leave the body node
            // silent mid-tick in some Chromium builds. 40 ms of slack after the envelope closes.
            setTimeout(() => {
                volume.disconnect();
                transientFilter.disconnect();
                bodyFilter.disconnect();
            }, tickLength * 1000 + 40);
        } catch {
            // Browsers can reject audio until the user interacts with the page; the ratchet tick
            // is decoration, not feedback, so a silent miss is acceptable.
        }
    };
})();
