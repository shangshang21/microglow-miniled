# 文件来源与许可 / Provenance and licensing

## 代码 / Code

本仓库新写的微光程序、补丁生成器和测试代码使用 GPL-3.0-only，许可全文见 LICENSE。维护者为 shangshang21，项目开发使用了 AI 编程助手；硬件拆机、照片和肉眼反馈来自维护者。

The newly written MicroGlow application, patch generator and tests are licensed under GPL-3.0-only; see LICENSE. The project is maintained by shangshang21 and was developed with AI coding assistance. The maintainer supplied the hardware observations and photos.

## 原厂固件 / Vendor firmware

原厂完整固件、修改后的完整镜像、分银行代码导出、私有原始日志和商业逆向数据库均不在本仓库中。兼容性文件只包含版本摘要、地址和少量结构校验值，不提供还原原固件的数据。生成器只在使用者本地处理其自有备份；生成的镜像仍含第三方程序，本项目许可不为其授予再发布权。

This repository excludes complete vendor firmware, patched full images, bank exports, private raw logs and commercial reverse-engineering databases. Compatibility metadata consists of digests, addresses and short structural checks, not data that reconstructs the original firmware. Generated images still contain third-party software. This project's license does not grant redistribution rights to that content.

## 照片 / Photographs

assets/ 中两张照片由维护者提供，用于记录本案例。照片未纳入 GPL 代码许可；其它使用请先联系维护者。未加入其它效果图。

The two photographs in assets/ were supplied by the maintainer for this case report. They are outside the code's GPL grant; contact the maintainer for other uses. No other result images are included.

## 系统接口与资料 / System interfaces and references

程序调用 Windows 的 DXVA2、CoreAudio 和本机 AMD ADL 接口，不附带微软或 AMD 的 DLL、SDK 头文件或运行库。接口结构和协议名称属于互操作说明，不能据此推断设备取得了任何厂商认证。

The application calls Windows DXVA2, CoreAudio and the locally installed AMD ADL interface. No Microsoft or AMD DLLs, SDK headers or runtimes are bundled. Interface descriptions do not imply vendor certification.

技术研究参考 Linux i915 的 Intel AUX 背光实现、AMD ADL 文档和 Windows 显示器控制文档。本公开包没有复制整份 Linux/SDK 源文件，也没有从私有档案复制第三方 GPU AUX 绑定程序。

Research references include Linux i915's Intel AUX backlight implementation, AMD ADL documentation and Windows monitor-control documentation. Full Linux/SDK sources and the third-party GPU AUX bindings from the private archive are not included in this public package.
