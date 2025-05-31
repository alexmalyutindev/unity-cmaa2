# CMAA2 for Unity URP

**Conservative Morphological Anti-Aliasing 2.0 (CMAA2)** ported to the Unity Universal Render Pipeline.

CMAA2 is a post-process anti-aliasing technique focused on delivering high-quality edge smoothing while preserving the sharpness of the original image.

Details of the original implementation and performance analysis are available in Intel’s article:  
https://www.intel.com/content/dam/develop/external/us/en/documents/conservative-morphological-anti-aliasing.pdf

| CMAA Off               | CMAA On               |
|------------------------|-----------------------|
| ![cmaa-2-disabled-out] | ![cmaa-2-enabled-out] |

## Installation

TODO:

## Acknowledgements

This project includes a modified version of [`CMAA2.hlsl`](https://github.com/GameTechDev/CMAA2/blob/master/Projects/CMAA2/CMAA2/CMAA2.hlsl) from Intel’s [GameTechDev/CMAA2](https://github.com/GameTechDev/CMAA2) project.  
The original code is licensed under the [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0).  
See [`THIRD_PARTY_LICENSES/CMAA2-LICENSE`](THIRD_PARTY_LICENSES/CMAA2-LICENSE) for details.

License
-------
This project is MIT License - see the [LICENSE](LICENSE) file for details


[cmaa-2-disabled-out]: https://github.com/user-attachments/assets/68805e27-e569-4da8-86ff-60912f0709b0
[cmaa-2-enabled-out]: https://github.com/user-attachments/assets/b41fb60c-af01-4f1f-83de-4f68b342e8cc
