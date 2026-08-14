import { User } from 'lucide-react';
import { useProfileImage } from './profileImageQuery';
import { useObjectUrl } from './useObjectUrl';
import './images.css';

type ProfileAvatarProps = {
  userId: string | undefined;
  displayName: string | undefined;
  className?: string;
  blob?: Blob;
  alt?: string;
};

export function ProfileAvatar({
  userId,
  displayName,
  className = '',
  blob,
  alt,
}: ProfileAvatarProps) {
  const query = useProfileImage(blob ? undefined : userId);
  const imageBlob = blob ?? query.data;
  const objectUrl = useObjectUrl(imageBlob);
  const initial = displayName?.trim().charAt(0).toUpperCase();

  return (
    <span className={`profile-avatar ${className}`.trim()} aria-hidden={alt ? undefined : true}>
      {objectUrl ? (
        <img className="profile-avatar-image" src={objectUrl} alt={alt ?? ''} />
      ) : initial ? (
        <span className="profile-avatar-initial">{initial}</span>
      ) : (
        <User size={18} />
      )}
    </span>
  );
}
