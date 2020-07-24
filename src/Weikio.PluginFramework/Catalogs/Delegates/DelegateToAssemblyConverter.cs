﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Weikio.PluginFramework.Catalogs.Delegates
{
    public class DelegateToAssemblyConverter
    {
        public Assembly CreateAssembly(MulticastDelegate multicastDelegate,
            List<(Predicate<ParameterInfo>, Func<ParameterInfo, ParameterConversion>)> conversionRules)
        {
            var id = DelegateCache.Add(multicastDelegate);

            var generator = new CodeToAssemblyGenerator();
            generator.ReferenceAssemblyContainingType<Action>();
            generator.ReferenceAssemblyContainingType<DelegateCache>();
            generator.ReferenceAssemblyContainingType<DelegateToAssemblyConverter>();

            var methodInfo = multicastDelegate.GetMethodInfo();

            if (methodInfo == null)
            {
                throw new Exception("Couldn't get method info from delegate");
            }

            var parameters = methodInfo.GetParameters();
            var returnType = methodInfo.ReturnType;

            var allTypes = new List<Type>();
            allTypes.AddRange(parameters.Select(x => x.ParameterType));
            allTypes.Add(returnType);

            var genTypes = new List<Type>();

            foreach (var type in allTypes)
            {
                genTypes = GetGenericTypes(type, genTypes);
            }

            foreach (var genType in genTypes)
            {
                allTypes.Add(genType);
            }

            foreach (var allType in allTypes)
            {
                generator.ReferenceAssembly(allType.Assembly);
            }

            var code = new StringBuilder();
            code.AppendLine("using System;");
            code.AppendLine("using System.Diagnostics;");
            code.AppendLine("using System.Threading.Tasks;");
            code.AppendLine("using System.Text;");
            code.AppendLine("using System.Collections;");
            code.AppendLine("using System.Collections.Generic;");

            foreach (var t in allTypes)
            {
                code.AppendLine($"using {t.Namespace};");
            }

            var constructorParameterNames = new List<string>();
            var methodParameterNamesWithTypes = new List<string>();
            var constructorParameterNamesWithTypes = new List<string>();
            var constructorFielsNamesWithTypes = new List<string>();
            var propertyNamesWithTypes = new List<string>();

            // var propertyNames = new List<string>();
            var delegateMethodParameters = new List<string>();

            if (conversionRules == null)
            {
                conversionRules = new List<(Predicate<ParameterInfo>, Func<ParameterInfo, ParameterConversion>)>();
            }

            for (var index = 0; index < parameters.Length; index++)
            {
                var parameterInfo = parameters[index];
                var parameterType = parameterInfo.ParameterType;

                var parameterName = parameterInfo.Name ??
                                    $"param{Guid.NewGuid().ToString().ToLowerInvariant().Replace("-", "")}";

                var handled = false;

                foreach (var conversionRule in conversionRules)
                {
                    var shouldRun = conversionRule.Item1(parameterInfo);

                    if (shouldRun)
                    {
                        var conversionResult = conversionRule.Item2(parameterInfo);

                        if (!string.IsNullOrWhiteSpace(conversionResult.Name))
                        {
                            parameterName = conversionResult.Name;
                        }

                        if (conversionResult.ToConstructor)
                        {
                            constructorParameterNames.Add(parameterName);
                            constructorParameterNamesWithTypes.Add($"{GetFriendlyName(parameterType)} {parameterName}");

                            var fieldName = $"_{parameterName}";
                            constructorFielsNamesWithTypes.Add($"{GetFriendlyName(parameterType)} {fieldName}");
                            delegateMethodParameters.Add(fieldName);

                            handled = true;

                            break;
                        }

                        if (conversionResult.ToPublicProperty)
                        {
                            var propertyName = $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parameterName)}";

                            if (string.Equals(parameterName, propertyName))
                            {
                                propertyName = $"{propertyName}Prop";
                            }

                            propertyNamesWithTypes.Add($"{GetFriendlyName(parameterType)} {propertyName}");
                            delegateMethodParameters.Add(propertyName);

                            handled = true;

                            break;
                        }

                        methodParameterNamesWithTypes.Add($"{GetFriendlyName(parameterType)} {parameterName}");

                        delegateMethodParameters.Add(parameterName);

                        handled = true;

                        break;
                    }
                }

                if (handled)
                {
                    continue;
                }

                methodParameterNamesWithTypes.Add($"{GetFriendlyName(parameterType)} {parameterName}");

                delegateMethodParameters.Add(parameterName);
            }

            code.AppendLine();
            code.AppendLine("namespace MyFuncTestNs");
            code.AppendLine("{");
            code.AppendLine("public class MyFuncTestClass");
            code.AppendLine("{");

            if (constructorParameterNames?.Any() == true)
            {
                foreach (var fieldNameWithType in constructorFielsNamesWithTypes)
                {
                    code.AppendLine($"private {fieldNameWithType};");
                }

                code.AppendLine($"public MyFuncTestClass({string.Join(", ", constructorParameterNamesWithTypes)})");
                code.AppendLine("{");

                foreach (var constructorParameterName in constructorParameterNames)
                {
                    code.AppendLine($"_{constructorParameterName} = {constructorParameterName};");
                }

                code.AppendLine("}"); // Close constructor
            }

            if (propertyNamesWithTypes?.Any() == true)
            {
                code.AppendLine();

                foreach (var fieldNameWithType in propertyNamesWithTypes)
                {
                    code.AppendLine($"public {fieldNameWithType} {{get; set;}}");
                }

                code.AppendLine();
            }

            code.AppendLine(
                $"public {GetFriendlyName(returnType)} Run ({string.Join(", ", methodParameterNamesWithTypes)})");
            code.AppendLine("{");
            code.AppendLine($"var deleg = ConsoleApp31.DelegateCache.Get(System.Guid.Parse(\"{id.ToString()}\"));");

            code.AppendLine(
                $"return ({GetFriendlyName(returnType)}) deleg.DynamicInvoke({string.Join(", ", delegateMethodParameters)});");
            code.AppendLine("}"); // Close method
            code.AppendLine("}"); // Close class
            code.AppendLine("}"); // Close namespace

            var s = code.ToString();
            var result = generator.Generate(s);

            return result;
        }

        public List<Type> GetGenericTypes(Type type, List<Type> types)
        {
            if (types == null)
            {
                types = new List<Type>();
            }

            if (!types.Contains(type))
            {
                types.Add(type);
            }

            if (!type.IsGenericType)
            {
                return types;
            }

            var typeParameters = type.GetGenericArguments();

            foreach (var typeParameter in typeParameters)
            {
                GetGenericTypes(typeParameter, types);
            }

            return types;
        }

        /// <summary>
        ///     https://stackoverflow.com/a/26429045/66988
        /// </summary>
        public static string GetFriendlyName(Type type)
        {
            var friendlyName = type.FullName;

            if (string.IsNullOrWhiteSpace(friendlyName))
            {
                friendlyName = type.Name;
            }

            if (!type.IsGenericType)
            {
                return friendlyName;
            }

            var iBacktick = friendlyName.IndexOf('`');

            if (iBacktick > 0)
            {
                friendlyName = friendlyName.Remove(iBacktick);
            }

            friendlyName += "<";
            var typeParameters = type.GetGenericArguments();

            for (var i = 0; i < typeParameters.Length; ++i)
            {
                var typeParamName = GetFriendlyName(typeParameters[i]);
                friendlyName += i == 0 ? typeParamName : "," + typeParamName;
            }

            friendlyName += ">";

            return friendlyName;
        }
    }
}
