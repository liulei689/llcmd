using CheckInProject.PersonDataCore.Interfaces;
using CheckInProject.PersonDataCore.Models;
using FaceRecognitionDotNet;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Drawing;

namespace CheckInProject.PersonDataCore.Implementation
{
    public class FaceDataManager : IFaceDataManager
    {
        public FaceRecognition FaceRecognitionAPI => Provider.GetRequiredService<FaceRecognition>();

        public CascadeClassifier Cascade => Provider.GetRequiredService<CascadeClassifier>();

        private readonly IServiceProvider Provider;

        public RawPersonDataBase CreateFaceData(Bitmap sourceData, string? sourceName, uint? personID)
        {
            using (var recognitionImage = FaceRecognition.LoadImage(sourceData))
            {
                var encodings = FaceRecognitionAPI.FaceEncodings(recognitionImage);
                if (encodings == null || encodings.Count() == 0)
                {
                    throw new InvalidOperationException("未能从图片中提取到人脸特征，请确保图片中包含清晰的人脸。");
                }
                var encoding = encodings.First().GetRawEncoding();
                var personName = sourceName;
                return new RawPersonDataBase { FaceEncoding = encoding, Name = personName, ClassID = personID };
            }
        }
        public IList<RawPersonDataBase> CreateFacesData(IList<Bitmap> sourceData)
        {
            var resultFaces = new List<RawPersonDataBase>();
            foreach (var item in sourceData)
            {
                using (var recognitionImage = FaceRecognition.LoadImage(item))
                {
                    var encodings = FaceRecognitionAPI.FaceEncodings(recognitionImage).Select(t => new RawPersonDataBase { FaceEncoding = t.GetRawEncoding() }).ToList();
                    resultFaces.AddRange(encodings);
                }
            }
            return resultFaces;
        }

        public IList<RawPersonDataBase> CompareFace(IList<RawPersonDataBase> faceDataList, RawPersonDataBase targetFaceData)
        {
            var faceEncodingList = faceDataList.Select(t => FaceRecognition.LoadFaceEncoding(t.FaceEncoding)).ToList();
            using (var targetFaceEncoding = FaceRecognition.LoadFaceEncoding(targetFaceData.FaceEncoding))
            {
                var recognizedFaces = FaceRecognition.CompareFaces(faceEncodingList, targetFaceEncoding, 0.4);
                var reconizedNames = new List<RawPersonDataBase>();
                var index = 0;
                foreach (var recognizedFace in recognizedFaces)
                {
                    if (recognizedFace)
                    {
                        var resultName = faceDataList[index];
                        reconizedNames.Add(resultName);
                    }
                    index++;
                }
                foreach (var item in faceEncodingList)
                {
                    item?.Dispose();
                }
                return reconizedNames;
            }
            
        }
        public IList<RawPersonDataBase> CompareFaces(IList<RawPersonDataBase> faceDataList, IList<RawPersonDataBase> targetFaceDataList)
        {
            var faceEncodingList = faceDataList.Select(t => FaceRecognition.LoadFaceEncoding(t.FaceEncoding)).ToList();
            var reconizedNames = new List<RawPersonDataBase>();
            foreach (var targetFaceData in targetFaceDataList)
            {
                using (var targetFaceEncoding = FaceRecognition.LoadFaceEncoding(targetFaceData.FaceEncoding))
                {
                    var recognizedFaces = FaceRecognition.CompareFaces(faceEncodingList, targetFaceEncoding, 0.4);
                    var index = 0;
                    foreach (var recognizedFace in recognizedFaces)
                    {
                        if (recognizedFace)
                        {
                            var resultName = faceDataList[index];
                            reconizedNames.Add(resultName);
                        }
                        index++;
                    }
                }
            }
            foreach (var item in faceEncodingList)
            {
                item?.Dispose();
            } 
            return reconizedNames;
        }
        public FaceCountModels GetFaceCount(Mat sourceImage)
        {
            // 使用更宽松的参数，提高戴眼镜等情况的识别率
            Rect[] recognizedFaces = Cascade.DetectMultiScale(
                            image: sourceImage,
                            scaleFactor: 1.05,
                            minNeighbors: 2,
                            flags: HaarDetectionTypes.DoRoughSearch | HaarDetectionTypes.ScaleImage,
                            minSize: new OpenCvSharp.Size(40, 40),
                            maxSize: new OpenCvSharp.Size()
                        );
            // ToBitmap()会创建独立的Bitmap副本，Mat可以安全释放
            using (var targetImage = sourceImage.Clone())
            {
                foreach (var recognizedFace in recognizedFaces)
                {
                    targetImage.Rectangle(recognizedFace, Scalar.GreenYellow, 2);
                }
                return new FaceCountModels() { RetangleImage = targetImage.ToBitmap(), Count = recognizedFaces.Length };
            }
        }

        public FaceRetangleModels GetFaceImage(Bitmap sourceBitmap)
        {
            // 不要将Mat放在using块中，因为返回的Bitmap需要它
            var sourceImage = sourceBitmap.ToMat();
            try
            {
                // 使用更宽松的参数，提高戴眼镜等情况的识别率
                Rect[] recognizedFaces = Cascade.DetectMultiScale(
                                            image: sourceImage,
                                            scaleFactor: 1.05,
                                            minNeighbors: 2,
                                            flags: HaarDetectionTypes.DoRoughSearch | HaarDetectionTypes.ScaleImage,
                                            minSize: new OpenCvSharp.Size(40, 40),
                                            maxSize: new OpenCvSharp.Size()
                                        );
                var maxiumRect = recognizedFaces.ToList().OrderByDescending(t => t.Height * t.Width).FirstOrDefault();
                
                // 检查是否检测到人脸
                if (maxiumRect == default(Rect) || maxiumRect.Width == 0 || maxiumRect.Height == 0)
                {
                    // 返回原图，不框选
                    var emptyResult = new FaceRetangleModels { FaceImages = new List<Bitmap>(), RetangleImage = sourceImage.ToBitmap() };
                    sourceImage.Dispose(); // 没有检测到人脸时立即释放
                    return emptyResult;
                }
                
                var resultList = new List<Bitmap>();
                using (var resultImage = new Mat(sourceImage, maxiumRect))
                {
                    resultList.Add(resultImage.ToBitmap());
                }
                sourceImage.Rectangle(maxiumRect, Scalar.GreenYellow, 5);
                return new FaceRetangleModels { FaceImages = resultList, RetangleImage = sourceImage.ToBitmap() };
            }
            catch
            {
                sourceImage?.Dispose();
                throw;
            }
        }

        public FaceRetangleModels GetFacesImage(Bitmap sourceBitmap)
        {
            // 不要将Mat放在using块中，因为返回的Bitmap需要它
            var sourceImage = sourceBitmap.ToMat();
            try
            {
                // 使用更宽松的参数，提高戴眼镜等情况的识别率
                Rect[] recognizedFaces = Cascade.DetectMultiScale(
                                            image: sourceImage,
                                            scaleFactor: 1.05,
                                            minNeighbors: 2,
                                            flags: HaarDetectionTypes.DoRoughSearch | HaarDetectionTypes.ScaleImage,
                                            minSize: new OpenCvSharp.Size(40, 40),
                                            maxSize: new OpenCvSharp.Size()
                                        );
                var resultList = new List<Bitmap>();
                foreach (var recognizedFace in recognizedFaces)
                {
                    using (var resultImage = new Mat(sourceImage, recognizedFace))
                    {
                        resultList.Add(resultImage.ToBitmap());
                        sourceImage.Rectangle(recognizedFace, Scalar.GreenYellow, 5);
                    }
                }
                return new FaceRetangleModels() { FaceImages = resultList, RetangleImage = sourceImage.ToBitmap() };
            }
            catch
            {
                sourceImage?.Dispose();
                throw;
            }
        }

        public FaceDataManager(IServiceProvider serviceProvider)
        {
            Provider = serviceProvider;
        }
    }
}
