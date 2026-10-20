import sys
import json
import warnings
import torch

# Suppress warnings
warnings.filterwarnings("ignore")

def main():
    if len(sys.argv) < 2:
        print("Usage: python vad_detect.py <audio_file_path>", file=sys.stderr)
        sys.exit(1)

    audio_path = sys.argv[1]

    try:
        import whisperx
    except ImportError:
        print(json.dumps({"error": "whisperx not installed in the current environment."}))
        sys.exit(1)

    try:
        # Load audio using whisperx
        audio = whisperx.load_audio(audio_path)
        
        # Load the VAD model from whisperx
        # We don't need a token because whisperx uses a local fallback model by default
        device = "cuda" if torch.cuda.is_available() else "cpu"
        
        # In whisperx 3.x, load_model returns the VAD model if specified, 
        # but actually we can just load the VAD pipeline directly or use load_model.
        vad_model = whisperx.vad.load_vad_model(device, vad_onset=0.500, vad_offset=0.363)
        
        # Run VAD
        # whisperx VAD takes a torch tensor or numpy array. Wait, whisperx.vad.load_vad_model
        # returns VoiceActivitySegmentation which takes a dict for pyannote.
        # It's better to use whisperx's wrapper: whisperx.vad.load_vad_model is used inside load_audio?
        # Actually, let's just use whisperx.load_audio and then vad_model({"waveform": torch.from_numpy(audio).unsqueeze(0), "sample_rate": 16000})
        # Wait, the easiest way is to use whisperx's load_model, wait, load_model is for ASR.
        # Let's see how WhisperXTranscriberService uses whisperx: it just runs `whisperx audio.mp3`.
        
        waveform = torch.from_numpy(audio).unsqueeze(0)
        input_dict = {"waveform": waveform, "sample_rate": 16000}
        
        segments = vad_model(input_dict)
        
        # Binarize segments
        # Let's use the Binarize wrapper from whisperx
        binarize = whisperx.vad.Binarize(max_duration=30.0, onset=0.5, offset=0.363)
        binarized = binarize(segments)
        
        speech_segments = []
        for speech_turn in binarized.get_timeline():
            speech_segments.append({
                "start": float(speech_turn.start),
                "end": float(speech_turn.end)
            })
            
        # Compute gaps
        gaps = []
        last_end = 0.0
        
        for seg in speech_segments:
            gap_duration = seg["start"] - last_end
            if gap_duration > 5.0: # 5 seconds minimum for instrumental
                gaps.append({
                    "start": last_end,
                    "end": seg["start"],
                    "duration": gap_duration
                })
            last_end = seg["end"]
            
        # We don't know the exact end of the audio, but we can return the total length from waveform
        total_duration = waveform.shape[1] / 16000.0
        if total_duration - last_end > 5.0:
            gaps.append({
                "start": last_end,
                "end": total_duration,
                "duration": total_duration - last_end
            })

        print(json.dumps({"gaps": gaps, "speech": speech_segments}))
        
    except Exception as e:
        print(json.dumps({"error": str(e)}))
        sys.exit(1)

if __name__ == "__main__":
    main()
