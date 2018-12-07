﻿/*
    Copyright (C) 2014 Omega software d.o.o.

    This file is part of Rhetos.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection.Emit;

namespace Rhetos.Dsl
{
    public class ConceptMember
    {
        public string Name { get; private set; }
        public Type ValueType { get; private set; }
        public bool IsConceptInfo { get; private set; }
        public bool IsKey { get; private set; }
        public int SortOrder1 { get; private set; }
        public int SortOrder2 { get; private set; }
        public bool IsDerived { get; private set; }
        public bool IsStringType { get; private set; }
        public bool IsParsable { get; private set; }

        private MemberInfo MemberInfo;

        public ConceptMember(MemberInfo memberInfo, ISet<string> nonParsableMembers)
        {
            this.MemberInfo = memberInfo;

            this.Name = memberInfo.Name;
            this.ValueType = GetMemberType(memberInfo);
            this.IsConceptInfo = typeof(IConceptInfo).IsAssignableFrom(ValueType);
            this.IsKey = memberInfo.GetCustomAttributes(typeof(ConceptKeyAttribute), false).Any();
            this.SortOrder1 = -InheritanceDepth(memberInfo); // This is needed when the derived type is not in the same assembly as the base type. In that case, MetadataToken in not enough for sorting all properties.
            this.SortOrder2 = memberInfo.MetadataToken;

            this.IsDerived = memberInfo.DeclaringType != memberInfo.ReflectedType;
            this.IsStringType = ValueType == typeof(string);

            this.IsParsable = nonParsableMembers == null || !nonParsableMembers.Contains(memberInfo.Name);
        }

        private static int InheritanceDepth(MemberInfo memberInfo)
        {
            var baseType = memberInfo.DeclaringType;
            var derivedType = memberInfo.ReflectedType;

            int depth = 0;
            while (baseType != derivedType)
            {
                depth++;
                if (derivedType.BaseType == null)
                    throw new FrameworkException("Unexpected IConceptInfo property inheritance: Property " + memberInfo.Name
                        + " is declared in type " + memberInfo.DeclaringType.FullName + " and used in type " + memberInfo.ReflectedType.FullName
                        + ", but declaring type is not a base type of the other." );
                derivedType = derivedType.BaseType;
            }
            return depth;
        }

        Action<IConceptInfo, object> _setValueFunc = null;

        public void SetMemberValue(IConceptInfo conceptInfo, object value)
        {
            if (_setValueFunc == null)
            {
                PropertyInfo pi;
                FieldInfo fi;

                Type fieldOrPropertyType = null;

                if ((pi = MemberInfo as PropertyInfo) != null)
                    fieldOrPropertyType = pi.PropertyType;
                else if ((fi = MemberInfo as FieldInfo) != null)
                    fieldOrPropertyType = fi.FieldType;
                else
                    throw new FrameworkException(
                        string.Format(CultureInfo.InvariantCulture,
                            "Unexpected member type {0} for member {1}.",
                                MemberInfo.MemberType,
                                MemberInfo.Name));

                var parameter1 = Expression.Parameter(typeof(IConceptInfo), "x");
                var parameter2 = Expression.Parameter(typeof(Object), "value");
                var castParameter1 = Expression.Convert(parameter1, MemberInfo.DeclaringType);
                UnaryExpression castParameter2 = Expression.Convert(parameter2, fieldOrPropertyType);
                var member = Expression.PropertyOrField(castParameter1, MemberInfo.Name);
                var assignment = Expression.Assign(member, castParameter2);
                var finalExpression = Expression.Lambda<Action<IConceptInfo, object>>(assignment, parameter1, parameter2);
                _setValueFunc = finalExpression.Compile();
            }

            _setValueFunc(conceptInfo, value);
        }

        private static Type GetMemberType(MemberInfo member)
        {
            PropertyInfo pi;
            if ((pi = member as PropertyInfo) != null)
                return pi.PropertyType;

            FieldInfo fi;
            if ((fi = member as FieldInfo) != null)
                return fi.FieldType;

            throw new FrameworkException(
                string.Format(CultureInfo.InvariantCulture,
                    "Unexpected member type {0} for member {1}.",
                        member.MemberType,
                        member.Name));
        }

        public override string ToString()
        {
            return Name + ":" + ValueType.Name;
        }

        Func<IConceptInfo, object> _getValueFunc = null;

        public object GetValue(IConceptInfo conceptInfo)
        {
            if (_getValueFunc == null)
            {
                if (MemberInfo is PropertyInfo || MemberInfo is FieldInfo)
                {
                    var parameter = Expression.Parameter(typeof(IConceptInfo), "x");
                    var castParameter = Expression.Convert(parameter, MemberInfo.DeclaringType);
                    var member = Expression.PropertyOrField(castParameter, MemberInfo.Name);                                                                     //Expression.Convert(member, MemberInfo)
                    var finalExpression = Expression.Lambda<Func<IConceptInfo, object>>(member, parameter);
                    _getValueFunc = finalExpression.Compile();
                }
                else
                    throw new FrameworkException(
                        string.Format(CultureInfo.InvariantCulture,
                            "Unexpected member type {0} for member {1}.",
                                MemberInfo.MemberType,
                                MemberInfo.Name));
            }

            return _getValueFunc(conceptInfo);
        }
    }
}
