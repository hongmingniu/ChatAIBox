Edge TTS protocol support includes a JavaScript adaptation of the Sec-MS-GEC v2
algorithm and constants from [rany2/edge-tts](https://github.com/rany2/edge-tts),
`src/edge_tts/drm.py`, retrieved November 18, 2026. These adaptations are in
`edge-gec.mjs` and `edge-constants.mjs` and are licensed under LGPL version 3
or later. The original license text is included in `EDGE-TTS-LICENSE.txt`.
Changes: JavaScript implementation, unsigned 32-bit arithmetic, shared use by
the Android and Windows clients. The editable source is included with this release.

Microsoft Edge voice services are provided by Microsoft. This application is
independent of Microsoft. Service availability and protocol may change.

Markdown rendering uses unified/remark/rehype (MIT), highlight.js (BSD-3-Clause),
KaTeX (MIT), and github-slugger (MIT). Their upstream licenses remain in the
dependency distributions. Other dependency notices are retained in packaged files.
