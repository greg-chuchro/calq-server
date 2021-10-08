using System;
using System.Collections;

namespace Calq.Server {
    internal class Reflection {
        public static object? GetFieldOrPropertyValue(object obj, string fieldOrPropertyName) {
            var type = obj.GetType();
            var field = type.GetField(fieldOrPropertyName);
            if (field != null) {
                return field.GetValue(obj);
            } else {
                var property = type.GetProperty(fieldOrPropertyName);
                if (property != null) {
                    return property.GetValue(obj);
                }
            }
            throw new MissingMemberException();
        }

        public static object? GetOrInitializeFieldOrPropertyValue(Type type, object obj, string fieldOrPropertyName) {
            object? value;
            var field = type.GetField(fieldOrPropertyName);
            if (field != null) {
                value = field.GetValue(obj);
                if (value == null) {
                    value = Activator.CreateInstance(field.FieldType);
                    field.SetValue(obj, value);
                }
                return value;
            } else {
                var property = type.GetProperty(fieldOrPropertyName);
                if (property != null) {
                    value = property.GetValue(obj);
                    if (value == null) {
                        value = Activator.CreateInstance(property.PropertyType);
                        property.SetValue(obj, value);
                    }
                    return value;
                }
            }
            throw new MissingMemberException();
        }

        public static object? InitializeFieldOrPropertyValue(Type type, object obj, string fieldOrPropertyName) {
            object? value;
            var field = type.GetField(fieldOrPropertyName);
            if (field != null) {
                value = Activator.CreateInstance(field.FieldType);
                field.SetValue(obj, value);
                return value;
            } else {
                var property = type.GetProperty(fieldOrPropertyName);
                if (property != null) {
                    value = Activator.CreateInstance(property.PropertyType);
                    property.SetValue(obj, value);
                    return value;
                }
            }
            throw new MissingMemberException();
        }

        public static void SetFieldOrPropertyValue(object obj, string fieldOrPropertyName, object? value) {
            var type = obj.GetType();
            SetFieldOrPropertyValue(type, obj, fieldOrPropertyName, value);
        }

        public static void SetFieldOrPropertyValue(Type type, object obj, string fieldOrPropertyName, object? value) {
            var field = type.GetField(fieldOrPropertyName);
            if (field != null) {
                field.SetValue(obj, value);
            } else {
                var property = type.GetProperty(fieldOrPropertyName);
                if (property != null) {
                    property.SetValue(obj, value);
                } else {
                    throw new MissingMemberException();
                }
            }
        }

        public static Type GetFieldOrPropertyType(object obj, string fieldOrPropertyName) {
            var type = obj.GetType();
            var field = type.GetField(fieldOrPropertyName);
            if (field != null) {
                return field.FieldType;
            } else {
                var property = type.GetProperty(fieldOrPropertyName);
                if (property != null) {
                    return property.PropertyType;
                }
            }
            throw new MissingMemberException();
        }

        public static bool IsPrimitive(ICollection collection) {
            return IsPrimitive(collection.GetType().GetGenericArguments()[0]);
        }

        public static bool IsPrimitive(object obj, string fieldOrPropertyName) {
            return IsPrimitive(GetFieldOrPropertyType(obj, fieldOrPropertyName));
        }

        public static bool IsPrimitive(Type type) {
            if (type.IsPrimitive || type == typeof(Decimal) || type == typeof(String)) {
                return true;
            }
            return false;
        }

        public static object ParseValue(Type type, string value) {
            object objValue;
            try {
                objValue = Type.GetTypeCode(type) switch {
                    TypeCode.Boolean => bool.Parse(value),
                    TypeCode.Byte => byte.Parse(value),
                    TypeCode.SByte => sbyte.Parse(value),
                    TypeCode.Char => char.Parse(value),
                    TypeCode.Decimal => decimal.Parse(value),
                    TypeCode.Double => double.Parse(value),
                    TypeCode.Single => float.Parse(value),
                    TypeCode.Int32 => int.Parse(value),
                    TypeCode.UInt32 => uint.Parse(value),
                    TypeCode.Int64 => long.Parse(value),
                    TypeCode.UInt64 => ulong.Parse(value),
                    TypeCode.Int16 => short.Parse(value),
                    TypeCode.UInt16 => ushort.Parse(value),
                    TypeCode.String => value,
                    _ => throw new ArgumentException($"type cannot be parsed: {type.Name}"),
                };
            } catch (OverflowException ex) {
                long min;
                ulong max;
                switch (Type.GetTypeCode(type)) {
                    case TypeCode.Byte:
                        min = byte.MinValue;
                        max = byte.MaxValue;
                        break;
                    case TypeCode.SByte:
                        min = sbyte.MinValue;
                        max = (ulong)sbyte.MaxValue;
                        break;
                    case TypeCode.Char:
                        min = char.MinValue;
                        max = char.MaxValue;
                        break;
                    case TypeCode.Int32:
                        min = int.MinValue;
                        max = int.MaxValue;
                        break;
                    case TypeCode.UInt32:
                        min = uint.MinValue;
                        max = uint.MaxValue;
                        break;
                    case TypeCode.Int64:
                        min = long.MinValue;
                        max = long.MaxValue;
                        break;
                    case TypeCode.UInt64:
                        min = (long)ulong.MinValue;
                        max = ulong.MaxValue;
                        break;
                    case TypeCode.Int16:
                        min = short.MinValue;
                        max = (ulong)short.MaxValue;
                        break;
                    case TypeCode.UInt16:
                        min = ushort.MinValue;
                        max = ushort.MaxValue;
                        break;
                    default:
                        throw;
                }
                throw new OverflowException($"{value} ({min}-{max})", ex);
            } catch (FormatException ex) {
                throw new FormatException($"value type mismatch: {value} is not {type.Name}", ex);
            }
            return objValue;
        }

        public static object? GetChildValue(ICollection collection, string key) {
            return collection switch {
                Array array => array.GetValue(int.Parse(key)),
                IList list => list[int.Parse(key)],
                IDictionary dictionary => dictionary[ParseValue(dictionary.GetType().GetGenericArguments()[0], key)],
                _ => throw new Exception("unsupported collection")
            };
        }

        public static void SetChildValue(ICollection collection, string key, object? value) {
            switch (collection) {
                case Array array:
                    array.SetValue(value, int.Parse(key));
                    break;
                case IList list:
                    list[int.Parse(key)] = value;
                    break;
                case IDictionary dictionary:
                    dictionary[ParseValue(dictionary.GetType().GetGenericArguments()[0], key)] = value;
                    break;
                default:
                    throw new Exception("unsupported collection");
            }
        }

        public static void AddChildValue(ICollection collection, object? value) {
            switch (collection) {
                case IList list:
                    list.Add(value);
                    break;
                default:
                    throw new Exception("unsupported collection");
            }
        }

        public static void DeleteChildValue(ICollection collection, string key) {
            switch (collection) {
                case IList list:
                    list.RemoveAt(int.Parse(key));
                    break;
                case IDictionary dictionary:
                    dictionary.Remove(key);
                    break;
                default:
                    throw new Exception("unsupported collection");
            }
        }
    }
}
