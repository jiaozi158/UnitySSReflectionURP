Documentation
=============

Setup
-------------

- Add **Screen Space Reflection URP** Renderer Feature to the active URP Renderer asset and adjust settings if needed.

 ![AddRendererFeature](https://github.com/jiaozi158/UnitySSReflectionURP/blob/main/Documentation/Images/Settings/URP_RendererFeature_SSR.png)

- Add **Lighting/Screen Space Reflection (URP)** to the scene's URP Volume.

 ![AddURPVolume](https://github.com/jiaozi158/UnitySSReflectionURP/blob/main/Documentation/Images/Settings/URP_Volume_SSR.png)

- Set the **State** to **Enabled** in Screen Space Reflection Volume.

- Adjust the settings in URP Volume and use different Volume types (global and local) to control SSR effect if needed.

- Make sure that there exists one Deferred renderer in the active URP asset to keep all Deferred shader variants in Forward path.

 ![AddDeferredToList](https://github.com/jiaozi158/UnitySSReflectionURP/blob/main/Documentation/Images/Settings/URP_KeepDeferredVariants.png)