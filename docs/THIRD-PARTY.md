# Third-party components

| Component | Where | License | Notes |
|---|---|---|---|
| VB-CABLE (VB-Audio) | `Voxel/Assets/vbcable` | Donationware | Redistributed under VB-Audio's distribution terms: the app shows VB-CABLE as a VB-Audio product, credits www.vb-cable.com and says it is donationware. See https://vb-audio.com/Services/licensing.htm |
| MeanVC2 (ASLP-lab) | `Voxel/Assets/ai/meanvc` (`asr.onnx`, `vc.onnx`, `vocos.onnx`) | Apache-2.0 | Real-time voice conversion behind the AI **Woman** and **Man** voices: Fast-U2++ content encoder, DiT converter and Vocos vocoder from https://huggingface.co/ASLP-lab/MeanVC2, converted to ONNX by `research/meanvc/export_meanvc.py`. The upstream model was trained partly on the Emilia dataset, some of which carries a non-commercial license; Voxel is free and non-commercial. |
| LibriSpeech (Panayotov et al.) | `Voxel/Assets/ai/meanvc/*.spk` | CC BY 4.0 | Each AI voice is a speaker embedding computed from one LibriSpeech recording: Woman = reader 1919, Man = reader 777. https://www.openslr.org/12 |
| LLVC (Koe AI) | `Voxel/Assets/ai/llvc-demo-c4.onnx` | MIT | The **Studio voice**: Koe AI's demo checkpoint exported to ONNX by `research/llvc_export.py`. https://github.com/KoeAI/LLVC |
| ONNX Runtime (Microsoft) | NuGet | MIT | Runs the AI models on the CPU. |
| NAudio | NuGet | MIT | Audio capture, playback, pitch shifting, biquad filters. |
| Tabler Icons | `Voxel/Icons.xaml` | MIT | Outline icons converted to WPF geometries. https://tabler.io/icons |

Used only on the developer's machine to make voice files (not shipped): WavLM-Large + ECAPA-TDNN speaker encoder from MeanVC2's preprocessing.
