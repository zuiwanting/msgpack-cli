﻿#region -- License Terms --
//
// MessagePack for CLI
//
// Copyright (C) 2010-2013 FUJIWARA, Yusuke
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//
#endregion -- License Terms --

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using MsgPack.Serialization.AbstractSerializers;
using MsgPack.Serialization.EmittingSerializers;

namespace MsgPack.Serialization
{
	// TODO: Enable this feature on WinRT using CodeDOM.
	/// <summary>
	///		Provides pre-compiled serialier assembly generation.
	/// </summary>
	/// <remarks>
	///		Currently, generated assembly has some restrictions:
	///		<list type="bullet">
	///			<item>
	///				The type name cannot be customize. It always to be <c>MsgPack.Serialization.EmittingSerializers.Generated.&lt;ESCAPED_TARGET_NAME&gt;Serializer</c>.
	///				Note that the <c>ESCAPED_TARGET_NAME</c> is the string generated by replacing type delimiters with undersecore ('_'). 
	///			</item>
	///			<item>
	///				The assembly cannot be used on WinRT because 
	///			</item>
	///		</list>
	///		<note>
	///			You should <strong>NOT</strong> assume at all like class hierarchy of generated type, its implementing interfaces, custom attributes, or dependencies.
	///			They subject to be changed in the future.
	///			If you want to get such fine grained control for them, you should implement own hand made serializers.
	///		</note>
	/// </remarks>
	public class SerializerGenerator
	{
		/// <summary>
		///		Gets the type of the root object which will be serialized/deserialized.
		/// </summary>
		/// <value>
		///		The first entry of <see cref="TargetTypes"/>.
		///		This value will be <c>null</c> when the <see cref="TargetTypes"/> is empty.
		/// </value>
		[Obsolete( "Use TargetTypes instead." )]
		public Type RootType
		{
			get { return this._targetTypes.FirstOrDefault(); }
		}

		private readonly HashSet<Type> _targetTypes;

		/// <summary>
		///		Gets target types will be generated dedicated serializers.
		/// </summary>
		/// <value>
		///		A collection which stores target types will be generated dedicated serializers.
		/// </value>
		public ICollection<Type> TargetTypes
		{
			get { return this._targetTypes; }
		}

		private readonly AssemblyName _assemblyName;

		/// <summary>
		///		Gets the name of the assembly to be generated.
		/// </summary>
		/// <value>
		///		The name of the assembly to be generated.
		/// </value>
		public AssemblyName AssemblyName
		{
			get { return this._assemblyName; }
		}

		private SerializationMethod _method;

		/// <summary>
		///		Gets or sets the <see cref="SerializationMethod"/> which indicates serialization method to be used.
		/// </summary>
		/// <value>
		///		The <see cref="SerializationMethod"/> which indicates serialization method to be used.
		/// </value>
		public SerializationMethod Method
		{
			get { return this._method; }
			set
			{

				if ( value != SerializationMethod.Array && value != SerializationMethod.Map )
				{
					throw new ArgumentOutOfRangeException( "value" );
				}
				this._method = value;
			}
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="SerializerGenerator"/> class.
		/// </summary>
		/// <param name="assemblyName">Name of the assembly to be generated.</param>
		/// <exception cref="ArgumentNullException">
		///		<paramref name="assemblyName"/> is <c>null</c>.
		/// </exception>
		public SerializerGenerator( AssemblyName assemblyName )
		{
			if ( assemblyName == null )
			{
				throw new ArgumentNullException( "assemblyName" );
			}

			Contract.EndContractBlock();

			this._assemblyName = assemblyName;
			this._targetTypes = new HashSet<Type>();
			this._method = SerializationMethod.Array;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="SerializerGenerator"/> class.
		/// </summary>
		/// <param name="rootType">Type of the root object which will be serialized/deserialized.</param>
		/// <param name="assemblyName">Name of the assembly to be generated.</param>
		/// <exception cref="ArgumentNullException">
		///		<paramref name="rootType"/> is <c>null</c>.
		///		Or <paramref name="assemblyName"/> is <c>null</c>.
		/// </exception>
		public SerializerGenerator( Type rootType, AssemblyName assemblyName )
			: this( assemblyName )
		{
			if ( rootType == null )
			{
				throw new ArgumentNullException( "rootType" );
			}

			Contract.EndContractBlock();

			this._targetTypes.Add( rootType );
		}

		/// <summary>
		///		Generates the serializer assembly and save it to current directory.
		///	</summary>
		/// <exception cref="IOException">Some I/O error is occurred on saving assembly file.</exception>
		public void GenerateAssemblyFile()
		{
			this.GenerateAssemblyFile(
				AppDomain.CurrentDomain.DefineDynamicAssembly( this._assemblyName, AssemblyBuilderAccess.RunAndSave ) );
		}

		/// <summary>
		///		Generates the serializer assembly and save it to specified directory.
		///	</summary>
		/// <param name="directory">The path of directory where newly generated assembly file will be located. If the directory does not exist, then it will be created.</param>
		/// <exception cref="ArgumentNullException"><paramref name="directory"/> is <c>null</c>.</exception>
		/// <exception cref="ArgumentException"><paramref name="directory"/> is not valid.</exception>
		/// <exception cref="PathTooLongException"><paramref name="directory"/> is too long.</exception>
		/// <exception cref="DirectoryNotFoundException"><paramref name="directory"/> is existent file.</exception>
		/// <exception cref="UnauthorizedAccessException">Cannot create specified directory for access control of file system.</exception>
		/// <exception cref="IOException">Some I/O error is occurred on creating directory or saving assembly file.</exception>
		public void GenerateAssemblyFile( string directory )
		{
			if ( !Directory.Exists( directory ) )
			{
				Directory.CreateDirectory( directory );
			}

			this.GenerateAssemblyFile(
				AppDomain.CurrentDomain.DefineDynamicAssembly( this._assemblyName, AssemblyBuilderAccess.RunAndSave, directory ) );
		}

		private void GenerateAssemblyFile( AssemblyBuilder assemblyBuilder )
		{
			var context = new SerializationContext();
			context.EmitterFlavor = EmitterFlavor.FieldBased;
			context.GeneratorOption = SerializationMethodGeneratorOption.CanDump;
			context.SerializationMethod = this._method;

			ISerializerCodeGenerationContext generationContext = null;

			foreach ( var targetType in this._targetTypes )
			{
				var builder =
					Activator.CreateInstance(
						typeof( AssemblyBuilderSerializerBuilder<> ).MakeGenericType( targetType ), assemblyBuilder 
					) as ISerializerCodeGenerator;

#if DEBUG
				Contract.Assert( builder != null );
#endif

				if( generationContext == null )
				{
					generationContext = builder.CreateGenerationContextForCodeGeneration( context );
				}

				builder.BuildSerializerCode( generationContext );
			}

			assemblyBuilder.Save( this._assemblyName.Name + ".dll" );
		}

		// TODO: Generate CodeDOM

		/// <summary>
		///		Defines non-generic interface of actual generic serializer builder.
		/// </summary>
		private abstract class Builder
		{
			protected Builder() { }

			/// <summary>
			///		Generates serializers using a specified assembly builder.
			/// </summary>
			/// <param name="context">The dedicated <see cref="SerializationContext"/>.</param>
			/// <param name="generatorManager">The dedicated <see cref="SerializationMethodGeneratorManager"/>.</param>
			public abstract void GenerateSerializerTo( SerializationContext context, SerializationMethodGeneratorManager generatorManager );
		}

		/// <summary>
		///		Actual generic serializer builder.
		/// </summary>
		/// <typeparam name="T">The type of root type.</typeparam>
		private class Builder<T> : Builder
		{
			public Builder() { }

			/// <summary>
			///		Generates the assembly and saves it to current directory.
			/// </summary>
			/// <param name="context">The dedicated <see cref="SerializationContext"/>.</param>
			/// <param name="generatorManager">The dedicated <see cref="SerializationMethodGeneratorManager"/>.</param>
			public override void GenerateSerializerTo( SerializationContext context, SerializationMethodGeneratorManager generatorManager )
			{
				var builder = context.SerializationMethod == SerializationMethod.Array ? new ArrayEmittingSerializerBuilder<T>( context ) as EmittingSerializerBuilder<T> : new MapEmittingSerializerBuilder<T>( context );
				builder.GeneratorManager = generatorManager;
				builder.CreateSerializer();
			}
		}
	}
}
