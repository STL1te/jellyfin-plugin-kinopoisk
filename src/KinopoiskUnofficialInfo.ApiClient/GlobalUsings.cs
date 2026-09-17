// NSwag derives the images endpoint enum name from its query parameter, so it lands as the
// unusable-looking `Type`. Alias it instead of hand-patching generated code on every regeneration.
global using KinopoiskImageType = KinopoiskUnofficialInfo.ApiClient.Type;
