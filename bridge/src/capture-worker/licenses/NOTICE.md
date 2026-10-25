# Runtime dependencies

- OpenCvSharp4 / OpenCvSharp4.runtime.win **4.13.0.20260627**, copyright 2008–2026, shimat and contributors. Apache-2.0. Package source commit: `b161e7e012f5101f6d5dc68a835c59db6cc88b18`. Source: https://github.com/shimat/opencvsharp
- OpenCV **4.13.0**, Apache-2.0. Source and third-party notices: https://github.com/opencv/opencv/tree/4.13.0
- The Windows runtime package supplies `opencv_videoio_ffmpeg4130_64.dll` alongside `OpenCvSharpExtern.dll`. FFmpeg LGPL-2.1 license is included; upstream binary/build source distribution repository: https://github.com/opencv/opencv_3rdparty and https://ffmpeg.org/legal.html . The capture worker uses still RGB images and does not call FFmpeg/video APIs.
- Newtonsoft.Json **13.0.3**, MIT, James Newton-King: https://github.com/JamesNK/Newtonsoft.Json/tree/13.0.3

The adjacent license texts are copied from the respective upstream projects. NuGet dependency versions, runtime files and their hashes are recorded by `build-capture-worker.ps1`.
