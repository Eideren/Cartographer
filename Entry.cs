using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Project.Collection;
using static System.Console;
using static Program;
// This is handled by setting current culture to invariant, we don't need culture distinction within this program
// ReSharper disable StringIndexOfIsCultureSpecific.1


partial class Node
{
    // (\+|-)? = could have one positive or negative sign  \d+ = continues with one or more digits  \.? = zero or one dot  \d* = zero or more digits
    const string MatchFloats = @"(-|\+)?\d+\.?\d*";
    
    static readonly List<Node> EmptyNode = new();
    static readonly List<string> Empty = new();
    static readonly double[] UVScalars = { 1.01723754425d, 1.00756565333d, 1.00994269627d };
    [ThreadStatic]
    static List<(double dist, string revRelPath, string path)>? _tempBuffer;

    public static readonly HashSet<string> TypesFiltered = new()
    {
        // AnimNodeSequence as is crashes UE4, look further into this if we actually need it
        "AnimNodeSequence"
    };
    static readonly HashSet<string> SupportedActors = new ()
    {
        "StaticMeshActor",
        "DirectionalLight",
        "PointLight",
        "SkyLight",
        "TdAreaLight",
        "RectLight",
        "Brush",
        "SkeletalMeshActor",
        "Note",
        "DecalActor",
        "AmbientSound",
        "TdReverbVolume",
        "AudioVolume",
        "InterpActor",
        "SpotLight"
    };
    /// <summary> UE4 ignores the actor name when parsing an actor with invalid components, here whitelist any valid components </summary>
    static readonly HashSet<string> SupportedObject = new ()
    {
        "StaticMeshComponent",
        "DirectionalLightComponent",
        "PointLightComponent",
        "SkyLightComponent",
        "RectLightComponent",
        "BrushComponent",
        "Polys",
        "SkeletalMeshComponent",
        "SceneComponent", // This is for 'Note' actor
        "DecalComponent",
        "AudioComponent",
        "SpotLightComponent"
    };
    
    void Convert(Node? parent, string prefixParam)
    {
        var isObject = Definition.StartsWith("Begin Object ");
        var isActor = isObject == false && Definition.StartsWith("Begin Actor ");
        var thisClass = KeywordAfter(Definition, " Class=");
        var thisName = KeywordAfter(Definition, " Name=");
        var thisOriginalName = thisName;


        if (thisClass != null)
        {
            if (isActor && SupportedActors.Contains(thisClass) == false) // Convert unsupported actors into note actors
            {
                TypesFiltered.Add(thisClass);
                
                // Dump children and prop into the note's text field
                var sw = new StringWriter();
                sw.WriteLine($"{thisClass} not supported, original content:");
                foreach (var child in (Children ?? EmptyNode))
                {
                    var output = child.Definition.Replace("Begin ", "");
                    output = output.Substring(0, output.Length < 24 ? output.Length : 24);
                    sw.WriteLine(output /* there's a max amount of char for text content ... */);
                }

                Children?.Clear();
                sw.Flush();
                var escapedString = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(sw.ToString(), false);
                
                AddProp("RootComponent=\"SceneComp\"");
                AddProp($"Text=\"{escapedString}\"");
                thisName += "_NotSupp";
                thisClass = "Note";
                Definition = $"Begin Actor Class={thisClass} Name={thisName}";
                
                Children ??= new List<Node>();
                Children.Add(new Node{ Definition = "Begin Object Class=SceneComponent Name=\"SceneComp\"", End = "End Object" } );
                // Do not return, continue conversion but as a note instead
            }
            else if(TypesFiltered.Contains(thisClass))
                return;
            else if (Definition.StartsWith("Begin Object ") && SupportedObject.Contains(thisClass) == false)
            {
                TypesFiltered.Add(thisClass);
                return;
            }
        }

        if (isActor && thisName != null)
        {
            var index = Definition.IndexOf(thisName);
            var prefix = Definition.Remove(index);
            var postfix = Definition.Substring(index + thisName.Length);
            thisName = $"{prefixParam}_{thisName}";
            Definition = $"{prefix}{thisName}{postfix}";
        }

        if (isObject && thisClass?.Contains("Light") == true && parent != null)
        {
            var p = parent.Properties?.FirstOrDefault(x => x.StartsWith("LightComponent"));
            parent.Properties?.Remove(p??"");

            Definition = Definition.Replace($" Name={thisName}", " Name=LightComponent0");

            parent.AddProp("LightComponent=\"LightComponent0\"");
            parent.AddProp("RootComponent=\"LightComponent0\"");
            AddProp("CastShadows=False");
            AddProp("Mobility=Static");
        }

        if (TryExtractKeywordAfter(Definition, "Begin Polygon Texture=", out var texRef)) // Fix material references for brushes
        {
            if (FindBestMatchForAsset(texRef, out var remappedUrl))
                Definition = Regex.Replace(Definition, @"Begin Polygon Texture=[^\s=]+"/*Replace start to the end of texture ref url with:*/, $"Begin Polygon Texture={remappedUrl}");
            else
                WriteLineC( ConsoleColor.Red, $"Could not find asset {texRef}" );
        }

        switch (thisClass)
        {
            case "TdReverbVolume":
                thisClass = "AudioVolume";
                Definition = Definition.Replace(" Class=TdReverbVolume ", " Class=AudioVolume ");
                break;
            case "InterpActor":
                thisClass = "StaticMeshActor";
                Definition = Definition.Replace(" Class=InterpActor ", " Class=StaticMeshActor ");
                var staticComp = Children?.FirstOrDefault( x => x.Definition.StartsWith( "Begin Object Class=StaticMeshComponent" ) );
                staticComp?.AddProp( "BodyInstance=(ObjectType=ECC_WorldDynamic,CollisionProfileName=\"BlockAllDynamic\",MaxAngularVelocity=3599.999756)" );
                staticComp?.AddProp( "Mobility=Movable" );
                break;
            case "TdAreaLight" when Children != null: // Area light to RectLight
                
                Definition = Definition
                    .Replace(" Class=TdAreaLight ", " Class=RectLight ")
                    .Replace(" Archetype=TdAreaLight'TdGame.Default__TdAreaLight'", " Archetype=/Script/Engine.RectLight'/Script/Engine.Default__RectLight'");
                foreach (var child in Children)
                {
                    child.Definition = child.Definition
                        .Replace(" Class=PointLightComponent ", " Class=RectLightComponent ")
                        .Replace(" Archetype=PointLightComponent'TdGame.Default__TdAreaLight:", " Archetype=RectLightComponent'/Script/Engine.Default__RectLight:");
                }
                break;
            case "SkeletalMeshActor" when Children != null: // Ensures that the first children of SkeletalMeshActor is the SkeletalMeshComponent
                var sComp = Children.First(x => x.Definition.StartsWith("Begin Object Class=SkeletalMeshComponent "));
                Children.Remove(sComp);
                Children.Insert(0, sComp);
                break;
            case "DecalComponent" when parent != null:
            {
                var width = Properties?.FirstOrDefault(x => x.StartsWith("Width=")) ?? "Width=200.0";
                var height = Properties?.FirstOrDefault(x => x.StartsWith("Height=")) ?? "Height=200.0";
                var widthVal = double.Parse(KeywordAfter(width, "Width=")!) / 2;
                var heightVal = double.Parse(KeywordAfter(height, "Height=")!) / 2;
                AddProp($"DecalSize=(X=30.0,Y={heightVal},Z={widthVal})");
                // UE4 decals are not oriented in the same way, rotate 90 degrees around X 
                var rot = parent.Properties?.FirstOrDefault(x => x.StartsWith("Rotation=")) ??
                          "Rotation=(Pitch=0,Yaw=0,Roll=0)";
                parent.Properties?.Remove(rot);
                var values = Regex.Matches(rot, @"\d+").Select(x => int.Parse(x.Value)).ToArray();
                values[2] = (values[2] + 65536 / 4) % 65536;
                parent.AddProp($"Rotation=(Pitch={values[0]},Yaw={values[1]},Roll={values[2]})");
                break;
            }
            case "AudioVolume" when parent != null && Properties?.FirstOrDefault(x => x.StartsWith("StereoAmbient")) is string stereoAmbientProp:
            {
                // Prop input looks like this:
                // StereoAmbient=(AmbientSound=SoundCue'A_Ambience.Stereo.AirDuct_02',Volume=1.000000,FadeInTime=1.000000,FadeOutTime=4.000000)
        
                ExtractPropFromString(stereoAmbientProp, out var name, out var value, out _);
        
                var str = @$"
      Begin Actor Class=AmbientSound Name={thisOriginalName}_AmbientSound Archetype=AmbientSound'Engine.Default__AmbientSound'
         Begin Object Class=AudioComponent Name=AudioComponent0 ObjName=AudioComponent_759 Archetype=AudioComponent'Engine.Default__AmbientSound:AudioComponent0'
            SoundCue=SoundCue'{KeywordAfter(value, "AmbientSound=SoundCue'")}'
            VolumeMultiplier={KeywordAfter(value, "Volume=") ?? "1.000000"}
            Name=" + $"\"AudioComponent_759\"" + $@"
            ObjectArchetype=AudioComponent'Engine.Default__AmbientSound:AudioComponent0'
         End Object
         bIsPlaying=True
         AudioComponent=AudioComponent'AudioComponent_759'
         Components(0)=AudioComponent'AudioComponent_759'
         Tag=" + "AmbientSound" + $@"
         {Properties?.FirstOrDefault( x => x.StartsWith("Location") )}
         Name=" + $"\"{thisOriginalName}_AmbientSound\"" + $@"
         ObjectArchetype=AmbientSound'Engine.Default__AmbientSound'
      End Actor";
                var sr = new StringReader(str);
                sr.ReadLine(); // skip initial empty line
                parent.Children!.Add( BuildTree(sr.ReadLine()!.TrimStart(), sr) );
                break;
            }
        }

        if (isActor) // Find and fix reference syntax, add quotes around value
        {
            for (int i = 0; Properties != null && i < Properties.Count; i++)
            {
                string prop = Properties[i];
                if (ExtractPropFromString(prop, out var name, out var stringVal, out var namedIndex) == false)
                    continue;

                if (Regex.Match(stringVal, @"(\w+)'(\w+)'").Groups is { Count: 3 }g)
                {
                    var className = g[1].Value;
                    var refObjName = g[2].Value;

                    if (Children?.FirstOrDefault(x => x.Definition.Contains(className) && x.Definition.Contains(refObjName)) is Node child)
                    {
                        var childRefedName = KeywordAfter(child.Definition, " Name=");
                        Properties[i] = $"{name}{namedIndex}=\"{childRefedName}\"";
                    }
                }
            }
        }

        for (int i = 0; Properties != null && i < Properties.Count; i++)
        {
            string prop = Properties[i];
            bool spaceDelimitedDeclaration = false; // Certain properties are space delimited instead of '=' delimited, like polygon config for brushes
            if (ExtractPropFromString(prop, out var name, out var stringVal, out var namedIndex) == false)
            {
                var indexOfSpace = prop.IndexOf(' ');
                if(indexOfSpace == -1)
                    continue; // prop is neither space nor '=' delimited, ignore it
                
                name = prop.Remove(indexOfSpace);
                stringVal = prop.Substring(indexOfSpace);
                spaceDelimitedDeclaration = true;
            }

            int dummyIndex = 0;
            Properties[i] = name switch
            {
                "bEnabled" when thisClass?.Contains("Light") == true
                    => $"bVisible={stringVal}",
                "LightingChannels" when stringVal.Contains("Static=False")
                    => "bVisible=False",
                "Radius" when isObject 
                    => $"AttenuationRadius={stringVal}",
                "StaticMesh" when isObject
                    => FixQuotedReference( prop ),
                "SkeletalMesh" when isObject 
                    => FixQuotedReference( prop ),
                "Parent" when isObject 
                    => FixQuotedReference( prop ),
                "Brightness" when thisClass == "PointLightComponent" || thisClass == "RectLightComponent"  || thisClass == "SpotLightComponent"
                    => $"Intensity={(double.Parse(stringVal) * 5000d):F6}",
                "Brightness" when thisClass == "DirectionalLightComponent" 
                    => $"Intensity={(double.Parse(stringVal) * Math.PI):F6}",
                "Brightness" when thisClass == "SkyLightComponent" 
                    => $"Intensity={stringVal}",
                "Tag" 
                    => $"Tags(0)={stringVal}",
                "Name" when thisName != null
                    => $"ActorLabel=\"{thisName}\"",
                "Name" when thisName == null
                    => $"ActorLabel={stringVal}",
                "CsgOper"
                    => ConvertCSGOperProp(stringVal),
                "Materials" when stringVal != "None"
                    => $"OverrideMaterials{namedIndex}={FixQuotedReference(stringVal)}",
                "DecalMaterial"
                    => $"DecalMaterial={FixQuotedReference(stringVal)}",
                "Origin" when spaceDelimitedDeclaration
                    => $"Origin{Regex.Replace( stringVal, MatchFloats, m => TransformOriginUV(m, dummyIndex++) )}",
                "TextureU" when spaceDelimitedDeclaration
                    => $"TextureU{Regex.Replace( stringVal, MatchFloats, TransformTexUV )}",
                "TextureV" when spaceDelimitedDeclaration
                    => $"TextureV{Regex.Replace( stringVal, MatchFloats, TransformTexUV )}",
                "SoundCue"
                    => $"Sound={FixQuotedReference(stringVal).Replace("SoundCue", "SoundWave")}",
                "Settings" when thisClass == "AudioVolume"
                    => $"Settings=(ReverbEffect=ReverbEffect'\"{BestMatchOrNull(KeywordAfter(stringVal, "ReverbType=") ?? "REVERB_Default") ?? "REVERB_Default"}\"',Volume={KeywordAfter(stringVal, "Volume=") ?? "0.5"},FadeTime={KeywordAfter(stringVal, "FadeTime=") ?? "2.0"})",
                "PrePivot"
                    => HandlePrePivot(stringVal),
                _ => Properties[i],
            };
        }


        // Import some parent properties into child object
        if (isObject && parent != null)
        {
            foreach (string prop in (parent.Properties??Empty))
            {
                if (ExtractPropFromString(prop, out var name, out var stringVal, out var index) == false)
                    continue;

                switch (name)
                {
                    case "Location": AddProp( $"RelativeLocation={stringVal}" ); break;
                    case "Rotation":
                        var remappedRot = Regex.Replace(stringVal, @"\d+", m =>
                        {
                            if (int.TryParse(m.Value, out var integer))
                            {
                                // Rotations are stored as 2bytes integers, a full rotation goes through the whole binary range, so 0->65536 = 0->360 degrees
                                // Replace each value of this vector through regex
                                return $"{(integer / 65536d * 360d):F6}";
                            }

                            return m.Value;
                        });
                        AddProp( $"RelativeRotation={remappedRot}" );
                        break;
                    case "DrawScale3D" when thisClass == "RectLightComponent":
                        var matches = Regex.Matches(stringVal, MatchFloats).ToArray();
                        AddProp( $"SourceWidth={matches[0]}" );
                        AddProp( $"SourceHeight={matches[2]}" );
                        break;
                    case "DrawScale3D":
                        AddProp( $"RelativeScale3D={stringVal}" );
                        break;
                    case "DrawScale":
                        AddProp( $"RelativeScale3D=(X={stringVal},Y={stringVal},Z={stringVal})" ); 
                        break;
                    case "bAutoPlay": AddProp( $"bAutoActivate={stringVal}" ); break;
                }
            }
        }

        if (Properties?.Where(x => x.StartsWith("RelativeScale3D")).ToArray() is string[] propArr && propArr.Length > 1)
        {
            // Aggregate scaling
            foreach (var s in propArr)
                Properties.Remove(s);
            (double x, double y, double z) finalValue = (1,1,1);
            foreach (string s in propArr)
            {
                ExtractPropFromString(s, out _, out var strVal, out _);
                var vals = Regex.Matches(strVal, MatchFloats).Select( x => x.Value ).ToArray();
                finalValue.x *= float.Parse(vals[0]);
                finalValue.y *= float.Parse(vals[1]);
                finalValue.z *= float.Parse(vals[2]);
            }
            
            AddProp($"RelativeScale3D=(X={finalValue.x},Y={finalValue.y},Z={finalValue.z})");
        }


        // Recurse into children
        if (Children != null)
        {
            for (int i = 0; i < Children.Count; i++)
                Children[i].Convert(this, prefixParam);
        }
    }
}



partial class Node
{
    public string Definition = "";
    public List<string>? Properties;
    public List<Node>? Children;
    public string End = "";



    public static Node BuildTree(string def, TextReader sIn)
    {
        var current = new Node{ Definition = def };
        string? line;
        while ((line = sIn.ReadLine()) != null)
        {
            line = line.TrimStart();
            if (line.StartsWith("End "))
            {
                current.End = line;
                return current;
            }
            if (line.StartsWith("Begin "))
            {
                current.Children ??= new List<Node>();
                current.Children.Add( BuildTree(line, sIn) );
                continue;
            }

            if (line != "")
            {
                current.Properties ??= new ();
                current.Properties.Add(line);
            }
        }

        return current;
    }



    public static void SetupFolders(List<Node> nodes, out string folderList)
    {
        int? firstLevel = null;
        bool init = false;
        folderList = "Begin FolderList\n";
        
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var level = node?.Children?.FirstOrDefault(x => x.Definition.StartsWith("Begin Level"));
            if (level == null || level.Children == null || level.Children.Count == 0)
                continue;
            
            if (firstLevel == null)
            {
                firstLevel = i;
                continue;
            }
            
            if (init == false)
            {
                init = true;
                var firstLevelIndex = firstLevel.Value;
                foreach (Node child in nodes[firstLevelIndex].Children!)
                    child.AddProp($"FolderPath=\"F_{firstLevelIndex}\"");
                folderList += $"\tFolder=F_{firstLevelIndex}\n";
            }
            foreach (Node child in level.Children)
                child.AddProp($"FolderPath=\"F_{i}\"");
            folderList += $"\tFolder=F_{i}\n";
        }

        folderList += "End FolderList"; 
    }



    public void ToString(TextWriter sOut, string prefix)
    {
        Convert(null, prefix);
        ToString(sOut, 0);
    }
    


    void AddProp( string str )
    {
        Properties ??= new();
        Properties.Add( str );
    }

    /// <summary>
    /// UE4 doesn't have pivot, include pivot through pos
    /// </summary>
    /// <param name="prePivotValues"></param>
    /// <returns></returns>
    string HandlePrePivot(string prePivotValues)
    {
        var values = Regex.Matches(prePivotValues, MatchFloats).Select(x => float.Parse(x.Value)).ToArray();
        if (Properties?.FindIndex(x => x.StartsWith("Location")) is int loc && loc != -1)
        {
            int index = 0;
            Properties[loc] = Regex.Replace(Properties[loc], MatchFloats, match => $"{float.Parse(match.Value) - values[index++]:F6}");
        }
        else
        {
            AddProp($"Location=(X={-values[0]},Y={-values[1]},Z={-values[2]})");
        }
        return "";
    }

    
    
    static string ConvertCSGOperProp(string stringVal)
    {
        var opTransformed = stringVal switch 
        {
            "CSG_Add" => "Brush_Add",
            "CSG_Subtract" => "Brush_Subtract",
            _ => "ERROR",
        };
        if (opTransformed == "ERROR")
        {
            WriteLineC( ConsoleColor.Red, $"No handling for parsing CSG operation '{stringVal}'" );
            opTransformed = stringVal;
        }

        return $"BrushType={opTransformed}";
    }



    // This is a gigantic hack, no idea exactly how to convert to UE4's values properly but it at least works on some of the brushes instead of none.
    // This is perhaps related to the used texture's resolution, not sure 
    string TransformOriginUV( Match m, int index )
    {
        var val = double.Parse(m.Value) * UVScalars[index];
        return ToUVFormat(val);
    }
    
    
    
    static string TransformTexUV( Match m )
    {
        var val = double.Parse(m.Value) * 0.774193780645d;
        return ToUVFormat(val);
    }

    
    
    static string ToUVFormat(double val)
    {
        var isNeg = val < 0;
        val = isNeg ? -val : val;
        var str = $"{val:F6}";
        str = str.PadLeft(12, '0');
        return isNeg ? $"-{str}" : $"+{str}";
    }
    
    

    static bool ExtractPropFromString( string str, out string name, out string value, out string? namedIndex )
    {
        var equalSignPos = str.IndexOf('=');
        if (equalSignPos == -1)
        {
            name = value = "";
            namedIndex = null;
            return false;
        }
        var nameLength = equalSignPos;
        namedIndex = null;
        var indexPos = str.IndexOf('(', 0, equalSignPos);
        if (indexPos != -1)
        {
            var indexEndPos = str.IndexOf(')', 0, equalSignPos);
            if (indexEndPos != -1)
            {
                namedIndex = str.Substring(indexPos, indexEndPos - indexPos+1);
                nameLength = indexPos;
            }
        }

        name = str.Substring(0, nameLength);
        value = str.Remove(0, equalSignPos+1);
        return true;
    }



    static string? KeywordAfter(string inputString, string delimiter)
    {
        TryExtractKeywordAfter(inputString, delimiter, out var output);
        return output;
    }



    static bool TryExtractKeywordAfter(string inputString, string delimiter, [MaybeNullWhen(false)]out string keyword)
    {
        var start = inputString.IndexOf(delimiter);
        if (start == -1)
        {
            keyword = null;
            return false;
        }

        start += delimiter.Length;

        int parenBalance = 0;
        int end = start;
        for (; end < inputString.Length; end++)
        {
            var c = inputString[end];
            if (char.IsWhiteSpace(c) || c == '=' || c == ',')
                break;
            if (c == '(')
                parenBalance++;
            if (c == ')')
                parenBalance--;
            
            if (parenBalance < 0)
                break;
        }

        keyword = inputString.Substring(start, end - start);
        return true;
    }



    void ToString(TextWriter sOut, int depth)
    {
        if( TryExtractKeywordAfter( Definition, " Class=", out var classType) )
        {
            if(TypesFiltered.Contains(classType))
                return;
            
            if (Definition.StartsWith("Begin Actor ") && SupportedActors.Contains(classType) == false)
            {
                TypesFiltered.Add(classType);
                return;
            }

            if (Definition.StartsWith("Begin Object ") && SupportedObject.Contains(classType) == false)
            {
                TypesFiltered.Add(classType);
                return;
            }
        }

        for (int i = 0; i < depth; i++)
            sOut.Write(' ');
        sOut.Write(Definition);
        sOut.Write('\n');
        
        var nextDepth = depth + 3;
        if (Children != null)
            foreach (var node in Children)
                node.ToString(sOut, nextDepth);

        if (Properties != null)
        {
            foreach (var prop in Properties)
            {
                for (int i = 0; i < nextDepth; i++)
                    sOut.Write(' ');
                sOut.Write(prop);
                sOut.Write('\n');
            }
        }
        
        for (int i = 0; i < depth; i++)
            sOut.Write(' ');
        sOut.Write(End);
        sOut.Write('\n');
    }




    static string? BestMatchOrNull(string url)
    {
        if (FindBestMatchForAsset(url, out var newUrl))
            return newUrl;
        return null;
    }



    static bool FindBestMatchForAsset(string url, [MaybeNullWhen(false)]out string? remappedUrl)
    {
        var relUrl = url.Replace('.', '\\').Replace('/', '\\');
        if (ResolvedMapping.TryGetValue(relUrl, out var output))
        {
            remappedUrl = output;
            return true;
        }

        // Find best match for this url, doing it in reverse since we need the filename to match before the directory names
        var filename = Path.GetFileName( relUrl );
    
        var charArray = relUrl.ToCharArray();
        Array.Reverse(charArray);
        var reversedUrl = new string( charArray );

        _tempBuffer ??= new();
        _tempBuffer.Clear();
        (double score, string revRelPath, string path)? bestScoring = null;
        foreach ( var (revRelPath, path) in Assets[ filename ] )
        {
            var score = FuzzyMatchScore(revRelPath, reversedUrl);

            if (bestScoring.HasValue == false || score > bestScoring.Value.score)
            {
                var tuple = (score, revRelPath, path);
                _tempBuffer.Add( tuple );
                bestScoring = tuple;
            }
        }

        if (bestScoring.HasValue == false) // Last resort, compare across all assets
        {
            var scores = Assets.AsParallel().Select(x =>
            {
                var (_, (revRelPath, path)) = x;
                var score = FuzzyMatchScore(reversedUrl, revRelPath) * 0.5 +
                            FuzzyMatchScore(filename, Path.GetFileName(path));

                return (revRelPath, path, score);
            }).ToArray();
            
            foreach (var (revRelPath, path, score) in scores)
            {
                //var score = FuzzyMatchScore(reversedUrl, revRelPath) * 0.5 + FuzzyMatchScore( filename, Path.GetFileName( path ) );
                if (bestScoring.HasValue == false || score > bestScoring.Value.score)
                {
                    var tuple = (score, revRelPath, path);
                    _tempBuffer.Add( tuple );
                    bestScoring = tuple;
                }
            }
        }

        foreach (var (score, revRelPath, path) in _tempBuffer)
        {
            if (bestScoring.HasValue == false)
                bestScoring = (score, revRelPath, path);
            else if (bestScoring.Value.score == score && bestScoring.Value.path != path)
                WriteLineC( ConsoleColor.Yellow,  $"\t{relUrl}: same matching score between '{bestScoring.Value.path}' and '{revRelPath}', using the former" );
            else 
                break;
        }
        
        if (bestScoring.HasValue == false)
        {
            remappedUrl = null;
            return false;
        }
        else
        {
            var path = bestScoring.Value.path;
            if (bestScoring.Value.revRelPath.Contains(reversedUrl) == false)
                WriteLineC( ConsoleColor.Yellow, $"\tBest match mapped '{relUrl}' to asset at path '{path}', verify that this is accurate" );
            
            remappedUrl = $"/Game/{path.Remove( 0, ContentDir.Length+1 ).Replace('\\', '/')}.{Path.GetFileName(path)/*No idea why unreal refers to their assets like so*/}";
            ResolvedMapping.Add(relUrl, remappedUrl);
            return true;
        }
    }


    static string FixQuotedReference(string line)
    {
        string pattern = "'[^']+";
        if (Regex.Match(line, pattern) is var regexMatch && regexMatch.Success == false)
        {
            WriteLineC( ConsoleColor.Red,  $"\tCould not fix reference within property {line}, failed regex." );
            return line;
        }
        var extractedUrl = regexMatch.Value.Remove(0, 1);
        
        if (FindBestMatchForAsset( extractedUrl, out var relUrl ))
        {
            return Regex.Replace(line, pattern, "'"+relUrl);
        }
        else
        {
            WriteLineC( ConsoleColor.Red, $"\tNo asset found for '{line}'" );
            return line;
        }
    }
    
    static double FuzzyMatchScore( string term, string against )
    {
        double scoreInOrder = 0;
        int jCarret = 0;
        for( int i = 0; i < term.Length; i++ )
        {
            for( int j = jCarret; j < against.Length; j++ )
            {
                bool match = term[ i ] == against[ j ];
                bool anyMatch = match || char.ToLower( term[ i ] ) == char.ToLower( against[ j ] );
                if( anyMatch )
                {
                    double baseScore = match ? 1d : 0.5d;
                    // Scale by amount of characters skipped
                    scoreInOrder += baseScore / ( 1 + j - jCarret );
                    jCarret = j+1;
                    break;
                }
            }
        }
        return scoreInOrder / term.Length;
    }
    
    static int LevenshteinDistance(string source, string target)
    {
        if (String.IsNullOrEmpty(source))
        {
            if (String.IsNullOrEmpty(target)) return 0;
            return target.Length;
        }
        if (String.IsNullOrEmpty(target)) return source.Length;

        if (source.Length > target.Length)
        {
            var temp = target;
            target = source;
            source = temp;
        }

        var m = target.Length;
        var n = source.Length;
        var distance = new int[2, m + 1];
        // Initialize the distance 'matrix'
        for (var j = 1; j <= m; j++) distance[0, j] = j;

        var currentRow = 0;
        for (var i = 1; i <= n; ++i)
        {
            currentRow = i & 1;
            distance[currentRow, 0] = i;
            var previousRow = currentRow ^ 1;
            for (var j = 1; j <= m; j++)
            {
                var cost = (target[j - 1] == source[i - 1] ? 0 : 1);
                distance[currentRow, j] = Math.Min(Math.Min(
                        distance[previousRow, j] + 1,
                        distance[currentRow, j - 1] + 1),
                    distance[previousRow, j - 1] + cost);
            }
        }
        return distance[currentRow, m];
    }
}



public class Program
{
    public static string ContentDir = "";
    public static KeyValues<string, (string revRelPath, string path)> Assets = KeyValues.NewString<(string revRelPath, string path)>();
    public static Dictionary<string, string> ResolvedMapping = new();

    public static void Main(string[] args)
    {
        if (Debugger.IsAttached)
        {
            SubMain(args);
        }
        else
        {
            try
            {
                SubMain(args);
            }
            catch (Exception e)
            {
                WriteLineC( ConsoleColor.Red, e.ToString() );
                ReadLine();
            }
        }
    }



    static void SubMain(string[] args)
    {
        // We don't need to worry about this stuff with these kinds of program
        CultureInfo.DefaultThreadCurrentUICulture 
            = CultureInfo.DefaultThreadCurrentCulture 
                = CultureInfo.CurrentUICulture 
                    = CultureInfo.CurrentCulture 
                        = CultureInfo.InvariantCulture;

        bool clipboard;
        string outputFile;
        StreamReader sIn;
        if (args.Length == 0 && (Environment.OSVersion.Platform == PlatformID.Win32S
                             || Environment.OSVersion.Platform == PlatformID.Win32Windows
                             || Environment.OSVersion.Platform == PlatformID.Win32NT
                             || Environment.OSVersion.Platform == PlatformID.WinCE))
        {
            var clipContent = WindowsClipboard.GetText();
            if (clipContent?.StartsWith("Begin Map") == true)
            {
                clipboard = true;
                WriteLine( "No file input provided, detected map in clipboard. Preparing conversion ..." );
            }
            else
            {
                while (true)
                {
                    WriteLine( $"No file input provided, use clipboard content instead ? (y/n) (Preview:'{clipContent?.Substring(0, clipContent.Length < 24 ? clipContent.Length : 24)}')" );
                    var r = ReadLine()??"";
                    if (r.ToLower() == "y")
                    {
                        clipboard = true;
                        break;
                    }
                    else if(r.ToLower() == "n")
                    {
                        clipboard = false;
                        break;
                    }
                }
            }
        }
        else
        {
            clipboard = false;
        }

        if (clipboard)
        {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(WindowsClipboard.GetText());
            writer.Flush();
            stream.Position = 0;
            sIn = new StreamReader(stream);
            outputFile = "clipboard";
        }
        else
        {
            string input;
            if (args.Length == 0)
            {
                input = "map.txt";
                WriteLine( $"No file input provided, defaulting to '{input}'" );
            }
            else
            {
                input = args[0];
            }
            input = input.Replace('/', '\\');

            outputFile = input.Insert( input.Length - Path.GetExtension( input ).Length, "_out" );
            sIn = new StreamReader(input);
        }

        if (clipboard || args.Length < 2)
        {
            WriteLine(clipboard
                ? "Running on clipboard, searching for 'Content' directory in working directory"
                : "No content directory provided, searching for 'Content' directory in working directory");
            ContentDir = Directory
                             .EnumerateDirectories(Environment.CurrentDirectory, "Content", SearchOption.TopDirectoryOnly)
                             .FirstOrDefault() 
                         // If none found in root, try all subdirs instead
                         ?? Directory
                             .EnumerateFiles(Environment.CurrentDirectory, "Content", SearchOption.AllDirectories)
                             .FirstOrDefault() ?? "";

            if (string.IsNullOrWhiteSpace(ContentDir))
            {
                WriteLineC( ConsoleColor.Red, "Could not find 'Content' directory within working directory, cannot continue" );
                return;
            }
        }
        else
        {
            ContentDir = args[1];
        }

        ContentDir = ContentDir.Replace('/', '\\');
        WriteLine( $"Scanning for assets within {ContentDir}" );

        foreach (var p in Directory.EnumerateFiles(ContentDir, "*.uasset", SearchOption.AllDirectories))
        {
            var path = p.Substring(0, p.Length - ".uasset".Length);
            var revRelPath = path.Remove(0, ContentDir.Length + 1);
            var charArray = revRelPath.ToCharArray();
            Array.Reverse(charArray);
            revRelPath = new string( charArray );
            Assets.Add( Path.GetFileNameWithoutExtension( p ), (revRelPath, path) );
        }

        if (Assets.Count == 0)
        {
            WriteLineC( ConsoleColor.Red, $"No assets found inside '{ContentDir}'" );
            return;
        }
        
        WriteLine( $"Found {Assets.Count} assets" );

        WriteLine( $"Fixing asset references into {outputFile} based on {ContentDir} ..." );

        TextWriter sOut = clipboard ? new StringWriter() : new StreamWriter(outputFile);
        
        using (sOut)
        {
            using (sIn)
            {
                string? line;
                List<Node> nodes = new List<Node>();
                while ((line = sIn.ReadLine()) != null)
                {
                    nodes.Add(Node.BuildTree(line, sIn));
                }

                Node.SetupFolders(nodes, out var folders);
                int prefix = 0;
                foreach (Node node in nodes)
                {
                    node.ToString( sOut, prefix++.ToString() );
                }
                sOut.WriteLine(folders);
            }
            if (clipboard)
            {
                sOut.Flush();
                WindowsClipboard.SetText( (sOut as StringWriter)!.ToString() );
            }
        }
        
        if(Node.TypesFiltered.Count != 0)
            WriteLine( $"Skipped unhandled types '{string.Join(", ", Node.TypesFiltered)}'" );
        WriteLine( "-----------" );
        if (clipboard)
            WriteLine( "Replaced clipboard text" );
        WriteLineC(ConsoleColor.Yellow, "Don't forget to use 'Validate Assets' on your assets folder in UE4 before importing the map, otherwise certain textures and materials won't show up");
        WriteLine( "Done, press enter to close" );
        ReadLine();
    }



    public static void WriteLineC( ConsoleColor color, string message )
    {
        var previous = ForegroundColor;
        ForegroundColor = color;
        WriteLine( message );
        ForegroundColor = previous;
    }
}
