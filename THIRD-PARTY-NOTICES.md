# Third-party notices

AudioSwitch is licensed under the MIT License (see `LICENSE`). The published executable includes, or the source is based on, the following components. Each is licensed under the MIT License; its copyright notice is listed below and the license text follows.

| Component | Use in AudioSwitch | Copyright |
|---|---|---|
| [.NET runtime](https://github.com/dotnet/runtime), [WPF](https://github.com/dotnet/wpf) and [Windows Forms](https://github.com/dotnet/winforms) | Bundled in the self-contained executable | Copyright (c) .NET Foundation and Contributors |
| [NAudio](https://github.com/naudio/NAudio) 3.3.0 | Bundled; Core Audio endpoints, test tone and level meter | Copyright 2008-2026 Mark Heath |
| [AudioDeviceCmdlets](https://github.com/frgnca/AudioDeviceCmdlets) | The `IPolicyConfig` interface declaration in `WindowsDefaultEndpointWriter.cs` follows its declaration | Copyright (c) 2016-2022 Francois Gendron |

The Python probe in `probes/` uses [comtypes](https://github.com/enthought/comtypes) and [pycaw](https://github.com/AndreMiras/pycaw) (both MIT), installed separately from `probes/requirements.txt`; they are not part of the executable. The tests use xUnit, which is not distributed.

## MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
