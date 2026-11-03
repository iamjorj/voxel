# Third-party components

| Component | Where | License | Notes |
|---|---|---|---|
| VB-CABLE (VB-Audio) | `Voxel/Assets/vbcable` | Donationware | Redistributed under VB-Audio's distribution terms: the app shows VB-CABLE as a VB-Audio product, credits www.vb-cable.com and says it is donationware. See https://vb-audio.com/Services/licensing.htm |
| MeanVC2 (ASLP-lab) | `Voxel/Assets/ai/meanvc` (`asr.onnx`, `vc.onnx`, `vocos.onnx`) | Apache-2.0 | Real-time voice conversion behind the AI **Man**, **E-girl** and **Grandpa** voices: Fast-U2++ content encoder, DiT converter and Vocos vocoder from https://huggingface.co/ASLP-lab/MeanVC2, converted to ONNX by `research/meanvc/export_meanvc.py`. The upstream model was trained partly on the Emilia dataset, some of which carries a non-commercial license; Voxel is free and non-commercial. |
| LibriSpeech (Panayotov et al.) | `Voxel/Assets/ai/meanvc/man.spk` | CC BY 4.0 | Man is a speaker embedding computed from one LibriSpeech recording by reader 777. https://www.openslr.org/12 |
| GLOBE V2 (Wang et al., from Mozilla Common Voice) | `Voxel/Assets/ai/meanvc/egirl.spk`, `grandpa.spk` | CC0 1.0 | E-girl and Grandpa are speaker embeddings computed from a few Common Voice clips of one speaker each: E-girl = speaker S_005141 (twenties), Grandpa = speaker S_005467 (sixties). Picked with `research/meanvc/find_voices.py`. https://huggingface.co/datasets/MushanW/GLOBE_V2 |
| ONNX Runtime (Microsoft) | NuGet | MIT | Runs the AI models on the CPU. |
| NAudio | NuGet | MIT | Audio capture, playback, pitch shifting, biquad filters. |
| Tabler Icons | `Voxel/Icons.xaml` | MIT | Outline icons converted to WPF geometries. https://tabler.io/icons |

Used only on the developer's machine to make voice files (not shipped): WavLM-Large + ECAPA-TDNN speaker encoder from MeanVC2's preprocessing.
